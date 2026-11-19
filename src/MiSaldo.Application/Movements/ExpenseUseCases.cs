using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Expenses;
namespace MiSaldo.Application.Movements;

public sealed record PaymentDto(Guid Id, string Value, DateOnly Date);

/// <summary>
/// An expense with its payments. <see cref="Status"/>, <see cref="TotalPaid"/>
/// and <see cref="Outstanding"/> are derived from the payments, never stored
/// (RF-GAS-03, RN-F04).
/// </summary>
public sealed record ExpenseDto(
    Guid Id,
    string Name,
    Guid CategoryId,
    string Budgeted,
    string Currency,
    DateOnly Date,
    ExpenseType Type,
    ExpenseStatus Status,
    string TotalPaid,
    string Outstanding,
    IReadOnlyList<PaymentDto> Payments);

public sealed record CreateExpenseCommand(string Name, Guid CategoryId, string Budgeted, DateOnly Date, ExpenseType Type);

public sealed record UpdateExpenseCommand(Guid Id, string Name, Guid CategoryId, string Budgeted, DateOnly Date, ExpenseType Type);

public sealed record AddPaymentCommand(Guid ExpenseId, string Value, DateOnly Date, string? IdempotencyKey);

/// <summary>The outcome of a payment request: <see cref="Replayed"/> when the idempotency key was seen before (CP-12).</summary>
public sealed record PaymentResult(ExpenseDto Expense, PaymentDto Payment, bool Replayed);

/// <summary>
/// The current user's expenses and their payments (RF-GAS-01 to RF-GAS-06).
/// Payments are an immutable history (RN-M08): they are added, never edited or
/// removed, and an expense that has payments is not deleted.
/// </summary>
public sealed class ExpenseHandlers
{
    public const int MaxNameLength = 256;
    public const int MaxIdempotencyKeyLength = 128;
    public const string NotFoundCode = "expense.not_found";
    public const string InvalidNameCode = "expense.invalid_name";
    public const string InvalidTypeCode = "expense.invalid_type";
    public const string InvalidCategoryCode = "expense.invalid_category";
    public const string HasPaymentsCode = "expense.has_payments";
    public const string InvalidIdempotencyKeyCode = "payment.invalid_idempotency_key";
    public const string IdempotencyMismatchCode = "payment.idempotency_mismatch";

    private readonly IExpenseRepository _expenses;
    private readonly ICategoryRepository _categories;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PeriodWriteRules _rules;
    private readonly ICurrentUserAccessor _currentUser;

    public ExpenseHandlers(
        IExpenseRepository expenses,
        ICategoryRepository categories,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        PeriodWriteRules rules,
        ICurrentUserAccessor currentUser)
    {
        _expenses = expenses;
        _categories = categories;
        _users = users;
        _unitOfWork = unitOfWork;
        _rules = rules;
        _currentUser = currentUser;
    }

    private static Error NotFound { get; } = Error.NotFound(NotFoundCode, "The expense was not found.");

    /// <summary>Expenses dated between the two dates, or with a payment between them.</summary>
    public async Task<Result<IReadOnlyList<ExpenseDto>>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<ExpenseDto>>(CurrentUserErrors.NoUser);
        }

        if (to < from)
        {
            return Result.Failure<IReadOnlyList<ExpenseDto>>(Error.Validation(
                Periods.PeriodHandlers.InvalidRangeCode, "The end date cannot be earlier than the start date."));
        }

        IReadOnlyList<ExpenseDto> list = (await _expenses.ListForRangeAsync(from, to, cancellationToken)).Select(ToDto).ToList();
        return Result.Success(list);
    }

    public async Task<Result<ExpenseDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure<ExpenseDto>(CurrentUserErrors.NoUser);
        }

        var expense = await _expenses.GetByIdAsync(id, cancellationToken);
        return expense is null ? Result.Failure<ExpenseDto>(NotFound) : Result.Success(ToDto(expense));
    }

    public async Task<Result<ExpenseDto>> CreateAsync(CreateExpenseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await CurrentSettings.LoadAsync(_currentUser, _users, cancellationToken) is not { } settings)
        {
            return Result.Failure<ExpenseDto>(CurrentUserErrors.NoUser);
        }

        if (await ValidateAsync(command.Name, command.Type, command.CategoryId, currentCategoryId: null, cancellationToken) is { } invalid)
        {
            return Result.Failure<ExpenseDto>(invalid);
        }

        var budgeted = AmountParser.Parse(command.Budgeted, settings.PrimaryCurrency);
        if (budgeted.IsFailure)
        {
            return Result.Failure<ExpenseDto>(budgeted.Error!);
        }

        if (await _rules.CheckAsync([command.Date], cancellationToken) is { } blocked)
        {
            return Result.Failure<ExpenseDto>(blocked);
        }

        var expense = new Expense(Guid.NewGuid(), command.Name.Trim(), command.CategoryId, budgeted.Value, command.Date, command.Type);
        _expenses.Add(expense);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(expense));
    }

    public async Task<Result<ExpenseDto>> UpdateAsync(UpdateExpenseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_currentUser.UserId is null)
        {
            return Result.Failure<ExpenseDto>(CurrentUserErrors.NoUser);
        }

        var expense = await _expenses.GetByIdAsync(command.Id, cancellationToken);
        if (expense is null)
        {
            return Result.Failure<ExpenseDto>(NotFound);
        }

        if (await ValidateAsync(command.Name, command.Type, command.CategoryId, expense.CategoryId, cancellationToken) is { } invalid)
        {
            return Result.Failure<ExpenseDto>(invalid);
        }

        var budgeted = AmountParser.Parse(command.Budgeted, expense.Budgeted.Currency);
        if (budgeted.IsFailure)
        {
            return Result.Failure<ExpenseDto>(budgeted.Error!);
        }

        // The obligation belongs to the expense date (RN-M02): both the old and the new period change.
        if (await _rules.CheckAsync([expense.Date, command.Date], cancellationToken) is { } blocked)
        {
            return Result.Failure<ExpenseDto>(blocked);
        }

        expense.Update(command.Name.Trim(), command.CategoryId, budgeted.Value, command.Date, command.Type);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(expense));
    }

    /// <summary>Deletes an expense without payments. One with payments keeps its history (RN-M08).</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure(CurrentUserErrors.NoUser);
        }

        var expense = await _expenses.GetByIdAsync(id, cancellationToken);
        if (expense is null)
        {
            return Result.Failure(NotFound);
        }

        if (expense.Payments.Count > 0)
        {
            return Result.Failure(Error.Conflict(
                HasPaymentsCode, "An expense with payments cannot be deleted; its payment history is kept."));
        }

        if (await _rules.CheckAsync([expense.Date], cancellationToken) is { } blocked)
        {
            return Result.Failure(blocked);
        }

        _expenses.Remove(expense);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Registers a payment. With an idempotency key, sending the same payment
    /// again returns the first one instead of creating another (RNF-DAT-03, CP-12).
    /// </summary>
    public async Task<Result<PaymentResult>> AddPaymentAsync(AddPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_currentUser.UserId is null)
        {
            return Result.Failure<PaymentResult>(CurrentUserErrors.NoUser);
        }

        var key = command.IdempotencyKey?.Trim();
        if (key is not null && (key.Length == 0 || key.Length > MaxIdempotencyKeyLength))
        {
            return Result.Failure<PaymentResult>(Error.Validation(
                InvalidIdempotencyKeyCode, $"The idempotency key must have between 1 and {MaxIdempotencyKeyLength} characters."));
        }

        var expense = await _expenses.GetByIdAsync(command.ExpenseId, cancellationToken);
        if (expense is null)
        {
            return Result.Failure<PaymentResult>(NotFound);
        }

        var value = AmountParser.Parse(command.Value, expense.Budgeted.Currency);
        if (value.IsFailure)
        {
            return Result.Failure<PaymentResult>(value.Error!);
        }

        if (key is not null && await ReplayAsync(key, command, expense, value.Value.Amount, cancellationToken) is { } earlier)
        {
            return earlier;
        }

        // A payment belongs to the period of its own date (RN-M02).
        if (await _rules.CheckAsync([command.Date], cancellationToken) is { } blocked)
        {
            return Result.Failure<PaymentResult>(blocked);
        }

        var payment = new ExpensePayment(Guid.NewGuid(), value.Value, command.Date);
        _expenses.AddPayment(expense, payment, key);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException) when (key is not null)
        {
            // The same key won a race in a parallel request: answer with the payment it stored.
            _unitOfWork.Discard();
            var reloaded = await _expenses.GetByIdAsync(command.ExpenseId, cancellationToken);
            if (reloaded is not null && await ReplayAsync(key, command, reloaded, value.Value.Amount, cancellationToken) is { } stored)
            {
                return stored;
            }

            throw;
        }

        return Result.Success(new PaymentResult(ToDto(expense), ToDto(payment), Replayed: false));
    }

    private async Task<Result<PaymentResult>?> ReplayAsync(
        string key, AddPaymentCommand command, Expense expense, decimal amount, CancellationToken cancellationToken)
    {
        var stored = await _expenses.FindPaymentByIdempotencyKeyAsync(key, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        // A reused key with a different payment is a client error, not a repeat.
        if (stored.ExpenseId != expense.Id || stored.Payment.Value.Amount != amount || stored.Payment.Date != command.Date)
        {
            return Result.Failure<PaymentResult>(Error.Conflict(
                IdempotencyMismatchCode, "This idempotency key was already used for a different payment."));
        }

        return Result.Success(new PaymentResult(ToDto(expense), ToDto(stored.Payment), Replayed: true));
    }

    private async Task<Error?> ValidateAsync(
        string? name, ExpenseType type, Guid categoryId, Guid? currentCategoryId, CancellationToken cancellationToken)
    {
        if (TextField.Validate(name, InvalidNameCode, "name", MaxNameLength) is { } invalidName)
        {
            return invalidName;
        }

        if (!Enum.IsDefined(type))
        {
            return Error.Validation(InvalidTypeCode, "The expense type is not valid.");
        }

        // The category must be one of the user's; an archived one can stay on old expenses but not be picked anew.
        var category = await _categories.GetByIdAsync(categoryId, cancellationToken);
        if (category is null || (category.IsArchived && categoryId != currentCategoryId))
        {
            return Error.Validation(InvalidCategoryCode, "The category does not exist or is archived.");
        }

        return null;
    }

    private static PaymentDto ToDto(ExpensePayment payment) =>
        new(payment.Id, MoneyText.Format(payment.Value), payment.Date);

    private static ExpenseDto ToDto(Expense expense) => new(
        expense.Id,
        expense.Name,
        expense.CategoryId,
        MoneyText.Format(expense.Budgeted),
        expense.Budgeted.Currency.Code,
        expense.Date,
        expense.Type,
        expense.Status(),
        MoneyText.Format(expense.TotalPaid()),
        MoneyText.Format(expense.Outstanding()),
        expense.Payments.OrderBy(p => p.Date).Select(ToDto).ToList());
}
