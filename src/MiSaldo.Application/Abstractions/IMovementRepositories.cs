using MiSaldo.Domain.Expenses;
using MiSaldo.Domain.Incomes;

namespace MiSaldo.Application.Abstractions;

/// <summary>Access to the current user's incomes.</summary>
public interface IIncomeRepository
{
    Task<IncomeTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Incomes relevant to a period: dated or received inside the range.</summary>
    Task<IReadOnlyList<IncomeTransaction>> ListForRangeAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>True when the user has any income at all (locks the primary currency, RF-PER-04).</summary>
    Task<bool> HasAnyAsync(CancellationToken cancellationToken = default);

    void Add(IncomeTransaction income);

    void Remove(IncomeTransaction income);
}

/// <summary>Access to the current user's expenses, always loaded with their payments.</summary>
public interface IExpenseRepository
{
    Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Expenses relevant to a period: dated or paid inside the range.</summary>
    Task<IReadOnlyList<Expense>> ListForRangeAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>True when the user has any expense at all (locks the primary currency, RF-PER-04).</summary>
    Task<bool> HasAnyAsync(CancellationToken cancellationToken = default);

    /// <summary>The payment stored with this idempotency key, if any (RNF-DAT-03, CP-12).</summary>
    Task<StoredPayment?> FindPaymentByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Adds a payment to the expense, keeping the optional idempotency key with it.</summary>
    void AddPayment(Expense expense, ExpensePayment payment, string? idempotencyKey);

    void Add(Expense expense);

    void Remove(Expense expense);
}

/// <summary>A stored payment and the expense it belongs to.</summary>
public sealed record StoredPayment(Guid ExpenseId, ExpensePayment Payment);
