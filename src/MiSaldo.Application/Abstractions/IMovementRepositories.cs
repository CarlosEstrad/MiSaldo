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

    void Add(Expense expense);

    void Remove(Expense expense);
}
