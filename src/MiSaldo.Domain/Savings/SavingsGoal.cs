using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Savings;

/// <summary>
/// A savings goal with a target amount and an optional desired date. Its balance
/// and progress are derived from the goal's movements, never stored (RF-AHO-01,
/// RF-AHO-05, RN-F10).
/// </summary>
public sealed class SavingsGoal
{
    public SavingsGoal(Guid id, string name, Money target, DateOnly? desiredDate = null)
    {
        if (!target.IsPositive)
        {
            throw new NonPositiveAmountException("Savings goal target", target.Amount);
        }

        Id = id;
        Name = name;
        Target = target;
        DesiredDate = desiredDate;
    }

    public Guid Id { get; }

    public string Name { get; }

    public Money Target { get; }

    public DateOnly? DesiredDate { get; }

    /// <summary>
    /// The current balance of this goal: the sum of its contributions minus the
    /// sum of its withdrawals (RN-F10). Movements of other goals or of the
    /// general fund are ignored.
    /// </summary>
    public Money CurrentBalance(IEnumerable<SavingsTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var balance = Money.Zero(Target.Currency);
        foreach (var txn in transactions)
        {
            if (txn.GoalId != Id)
            {
                continue;
            }

            balance = txn.IsContribution
                ? balance + txn.Value
                : balance - txn.Value;
        }

        return balance;
    }

    /// <summary>
    /// Progress toward the target as a percentage (0 to 100, not capped). Returns
    /// 0 when the target is zero, which cannot happen for a valid goal but keeps
    /// the method safe (RF-AHO-05).
    /// </summary>
    public decimal ProgressPercent(IEnumerable<SavingsTransaction> transactions)
    {
        if (Target.Amount == 0m)
        {
            return 0m;
        }

        return CurrentBalance(transactions).Amount / Target.Amount * 100m;
    }
}
