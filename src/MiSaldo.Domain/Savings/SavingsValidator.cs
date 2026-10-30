using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Savings;

/// <summary>
/// Validates savings movements against the period's available money and the
/// goal's balance, then builds the movement. A contribution must not exceed the
/// money available in the period (D-10, RF-AHO-08): it must be less than or
/// equal to it. A withdrawal must not exceed the available savings balance
/// (RF-AHO-04). These rules depend on amounts computed outside the goal (the
/// period's available money), so they live here rather than on the entities.
/// </summary>
public static class SavingsValidator
{
    /// <summary>
    /// Builds a validated contribution. <paramref name="availableMoney"/> is the
    /// money available in the period before this contribution. Throws
    /// <see cref="SavingsExceedsAvailableException"/> when the contribution
    /// exceeds it (R6.2, CP-18, CP-19).
    /// </summary>
    public static SavingsTransaction CreateContribution(
        Guid id,
        Money value,
        DateOnly date,
        Money availableMoney,
        Guid? goalId = null)
    {
        if (value > availableMoney)
        {
            throw new SavingsExceedsAvailableException(value, availableMoney);
        }

        return new SavingsTransaction(id, value, date, isContribution: true, goalId);
    }

    /// <summary>
    /// Builds a validated withdrawal. <paramref name="availableBalance"/> is the
    /// balance of the goal (or the general fund) the withdrawal draws from.
    /// Throws <see cref="WithdrawalExceedsSavingsException"/> when the withdrawal
    /// exceeds it (R6.3, RF-AHO-04).
    /// </summary>
    public static SavingsTransaction CreateWithdrawal(
        Guid id,
        Money value,
        DateOnly date,
        Money availableBalance,
        Guid? goalId = null)
    {
        if (value > availableBalance)
        {
            throw new WithdrawalExceedsSavingsException(value, availableBalance);
        }

        return new SavingsTransaction(id, value, date, isContribution: false, goalId);
    }
}
