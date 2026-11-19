using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Incomes;
namespace MiSaldo.Application.Movements;

/// <summary>An income as the client sees it. The value is a decimal string (RNF-DAT-02).</summary>
public sealed record IncomeDto(
    Guid Id,
    string Description,
    string Value,
    string Currency,
    DateOnly Date,
    string Source,
    IncomeStatus Status,
    DateOnly? ReceivedDate);

/// <summary>
/// A new income. When <see cref="Received"/> is true it counts toward the cash
/// on <see cref="ReceivedDate"/>, or on <see cref="Date"/> if none is given
/// (RF-ING-02, RF-ING-03).
/// </summary>
public sealed record CreateIncomeCommand(string Description, string Value, DateOnly Date, string Source, bool Received, DateOnly? ReceivedDate);

public sealed record UpdateIncomeCommand(Guid Id, string Description, string Value, DateOnly Date, string Source);

/// <summary>
/// The current user's incomes (RF-ING-01 to RF-ING-05). Indicators are never
/// stored, so every change shows up in the next dashboard read (RF-PRD-08).
/// </summary>
public sealed class IncomeHandlers
{
    public const int MaxDescriptionLength = 256;
    public const int MaxSourceLength = 128;
    public const string NotFoundCode = "income.not_found";
    public const string InvalidDescriptionCode = "income.invalid_description";
    public const string InvalidSourceCode = "income.invalid_source";

    private readonly IIncomeRepository _incomes;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PeriodWriteRules _rules;
    private readonly ICurrentUserAccessor _currentUser;

    public IncomeHandlers(
        IIncomeRepository incomes,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        PeriodWriteRules rules,
        ICurrentUserAccessor currentUser)
    {
        _incomes = incomes;
        _users = users;
        _unitOfWork = unitOfWork;
        _rules = rules;
        _currentUser = currentUser;
    }

    private static Error NotFound { get; } = Error.NotFound(NotFoundCode, "The income was not found.");

    /// <summary>Incomes dated or received between the two dates, inclusive.</summary>
    public async Task<Result<IReadOnlyList<IncomeDto>>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<IncomeDto>>(CurrentUserErrors.NoUser);
        }

        if (to < from)
        {
            return Result.Failure<IReadOnlyList<IncomeDto>>(Error.Validation(
                Periods.PeriodHandlers.InvalidRangeCode, "The end date cannot be earlier than the start date."));
        }

        IReadOnlyList<IncomeDto> list = (await _incomes.ListForRangeAsync(from, to, cancellationToken)).Select(ToDto).ToList();
        return Result.Success(list);
    }

    public async Task<Result<IncomeDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure<IncomeDto>(CurrentUserErrors.NoUser);
        }

        var income = await _incomes.GetByIdAsync(id, cancellationToken);
        return income is null ? Result.Failure<IncomeDto>(NotFound) : Result.Success(ToDto(income));
    }

    public async Task<Result<IncomeDto>> CreateAsync(CreateIncomeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await CurrentSettings.LoadAsync(_currentUser, _users, cancellationToken) is not { } settings)
        {
            return Result.Failure<IncomeDto>(CurrentUserErrors.NoUser);
        }

        if (ValidateText(command.Description, command.Source) is { } invalid)
        {
            return Result.Failure<IncomeDto>(invalid);
        }

        var value = AmountParser.Parse(command.Value, settings.PrimaryCurrency);
        if (value.IsFailure)
        {
            return Result.Failure<IncomeDto>(value.Error!);
        }

        DateOnly? receivedDate = command.Received ? command.ReceivedDate ?? command.Date : null;
        var dates = receivedDate is { } received ? new[] { command.Date, received } : [command.Date];
        if (await _rules.CheckAsync(dates, cancellationToken) is { } blocked)
        {
            return Result.Failure<IncomeDto>(blocked);
        }

        var income = new IncomeTransaction(
            Guid.NewGuid(),
            command.Description.Trim(),
            value.Value,
            command.Date,
            command.Source.Trim(),
            command.Received ? IncomeStatus.Received : IncomeStatus.Planned,
            receivedDate);
        _incomes.Add(income);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(income));
    }

    public async Task<Result<IncomeDto>> UpdateAsync(UpdateIncomeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_currentUser.UserId is null)
        {
            return Result.Failure<IncomeDto>(CurrentUserErrors.NoUser);
        }

        if (ValidateText(command.Description, command.Source) is { } invalid)
        {
            return Result.Failure<IncomeDto>(invalid);
        }

        var income = await _incomes.GetByIdAsync(command.Id, cancellationToken);
        if (income is null)
        {
            return Result.Failure<IncomeDto>(NotFound);
        }

        // The income keeps the currency it was stored with; the primary currency is locked anyway (RF-PER-04).
        var value = AmountParser.Parse(command.Value, income.Value.Currency);
        if (value.IsFailure)
        {
            return Result.Failure<IncomeDto>(value.Error!);
        }

        // Both the old and the new dates must be open: moving an income changes both periods.
        if (await _rules.CheckAsync(DatesOf(income).Append(command.Date), cancellationToken) is { } blocked)
        {
            return Result.Failure<IncomeDto>(blocked);
        }

        income.Update(command.Description.Trim(), value.Value, command.Date, command.Source.Trim());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(income));
    }

    /// <summary>Marks the income as received on the date given, or today (RF-ING-03).</summary>
    public async Task<Result<IncomeDto>> MarkReceivedAsync(Guid id, DateOnly? receivedDate, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure<IncomeDto>(CurrentUserErrors.NoUser);
        }

        var income = await _incomes.GetByIdAsync(id, cancellationToken);
        if (income is null)
        {
            return Result.Failure<IncomeDto>(NotFound);
        }

        var date = receivedDate ?? _rules.Today;
        if (await _rules.CheckAsync(DatesOf(income).Append(date), cancellationToken) is { } blocked)
        {
            return Result.Failure<IncomeDto>(blocked);
        }

        income.MarkReceived(date);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(income));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure(CurrentUserErrors.NoUser);
        }

        var income = await _incomes.GetByIdAsync(id, cancellationToken);
        if (income is null)
        {
            return Result.Failure(NotFound);
        }

        if (await _rules.CheckAsync(DatesOf(income), cancellationToken) is { } blocked)
        {
            return Result.Failure(blocked);
        }

        _incomes.Remove(income);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static IEnumerable<DateOnly> DatesOf(IncomeTransaction income) =>
        income.ReceivedDate is { } received ? [income.Date, received] : [income.Date];

    private static Error? ValidateText(string? description, string? source) =>
        TextField.Validate(description, InvalidDescriptionCode, "description", MaxDescriptionLength)
        ?? TextField.Validate(source, InvalidSourceCode, "source", MaxSourceLength);

    private static IncomeDto ToDto(IncomeTransaction income) => new(
        income.Id,
        income.Description,
        MoneyText.Format(income.Value),
        income.Value.Currency.Code,
        income.Date,
        income.Source,
        income.Status,
        income.ReceivedDate);
}
