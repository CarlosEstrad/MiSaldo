using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Savings;

/// <summary>
/// A savings movement: a contribution into savings or a withdrawal out of it.
/// A contribution is a transfer between the user's own funds, never an expense
/// (RN-M04, RF-AHO-03). Movements are immutable (RN-M08). A null
/// <see cref="GoalId"/> means the general savings fund rather than a specific
/// goal (RF-AHO-02). The value must be strictly positive.
/// </summary>
public sealed class SavingsTransaction
{
    public SavingsTransaction(
        Guid id,
        Money value,
        DateOnly date,
        bool isContribution,
        Guid? goalId = null)
    {
        if (!value.IsPositive)
        {
            throw new NonPositiveAmountException("Savings movement value", value.Amount);
        }

        Id = id;
        Value = value;
        Date = date;
        IsContribution = isContribution;
        GoalId = goalId;
    }

    public Guid Id { get; }

    public Money Value { get; }

    /// <summary>The movement date. It belongs to the period that contains it (RN-M02).</summary>
    public DateOnly Date { get; }

    /// <summary>True for a contribution (into savings), false for a withdrawal.</summary>
    public bool IsContribution { get; }

    /// <summary>The goal this movement belongs to, or null for the general fund.</summary>
    public Guid? GoalId { get; }
}
