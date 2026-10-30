using MiSaldo.Domain.Common;
using MiSaldo.Domain.Savings;
using Xunit;

namespace MiSaldo.Domain.Tests.Savings;

public class SavingsValidatorTests
{
    private static readonly DateOnly Date = new(2026, 1, 15);

    private static Money Cop(decimal amount) => new(amount, Currency.Cop);

    // --- Contributions (R6.2, D-10) ---

    // CP-18: contribution 300,000 with available 200,000 -> rejected
    [Fact]
    public void Cp18_contribution_above_available_is_rejected()
    {
        var ex = Assert.Throws<SavingsExceedsAvailableException>(() =>
            SavingsValidator.CreateContribution(
                Guid.NewGuid(), Cop(300_000m), Date, availableMoney: Cop(200_000m)));

        Assert.Equal(300_000m, ex.Contribution.Amount);
        Assert.Equal(200_000m, ex.AvailableMoney.Amount);
    }

    // CP-19: contribution 200,000 with available 200,000 -> accepted
    [Fact]
    public void Cp19_contribution_equal_to_available_is_accepted()
    {
        var txn = SavingsValidator.CreateContribution(
            Guid.NewGuid(), Cop(200_000m), Date, availableMoney: Cop(200_000m));

        Assert.True(txn.IsContribution);
        Assert.Equal(200_000m, txn.Value.Amount);
    }

    [Fact]
    public void Contribution_below_available_is_accepted()
    {
        var txn = SavingsValidator.CreateContribution(
            Guid.NewGuid(), Cop(150_000m), Date, availableMoney: Cop(200_000m));

        Assert.Equal(150_000m, txn.Value.Amount);
    }

    [Fact]
    public void Contribution_can_target_a_goal()
    {
        var goalId = Guid.NewGuid();
        var txn = SavingsValidator.CreateContribution(
            Guid.NewGuid(), Cop(100_000m), Date, availableMoney: Cop(200_000m), goalId);

        Assert.Equal(goalId, txn.GoalId);
    }

    // --- Withdrawals (R6.3, RF-AHO-04) ---

    [Fact]
    public void Withdrawal_above_balance_is_rejected()
    {
        var ex = Assert.Throws<WithdrawalExceedsSavingsException>(() =>
            SavingsValidator.CreateWithdrawal(
                Guid.NewGuid(), Cop(300_000m), Date, availableBalance: Cop(200_000m)));

        Assert.Equal(300_000m, ex.Withdrawal.Amount);
        Assert.Equal(200_000m, ex.AvailableBalance.Amount);
    }

    [Fact]
    public void Withdrawal_equal_to_balance_is_accepted()
    {
        var txn = SavingsValidator.CreateWithdrawal(
            Guid.NewGuid(), Cop(200_000m), Date, availableBalance: Cop(200_000m));

        Assert.False(txn.IsContribution);
        Assert.Equal(200_000m, txn.Value.Amount);
    }

    [Fact]
    public void Withdrawal_below_balance_is_accepted()
    {
        var txn = SavingsValidator.CreateWithdrawal(
            Guid.NewGuid(), Cop(50_000m), Date, availableBalance: Cop(200_000m));

        Assert.Equal(50_000m, txn.Value.Amount);
    }

    // The underlying entity rules still apply (value > 0)
    [Fact]
    public void Non_positive_contribution_is_still_rejected()
    {
        Assert.Throws<NonPositiveAmountException>(() =>
            SavingsValidator.CreateContribution(
                Guid.NewGuid(), Cop(0m), Date, availableMoney: Cop(200_000m)));
    }
}
