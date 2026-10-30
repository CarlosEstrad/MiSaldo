using MiSaldo.Domain.Common;
using MiSaldo.Domain.Savings;
using Xunit;

namespace MiSaldo.Domain.Tests.Savings;

public class SavingsTests
{
    private static readonly DateOnly Date = new(2026, 1, 10);

    private static SavingsTransaction Contribution(decimal amount, Guid? goalId) =>
        new(Guid.NewGuid(), new Money(amount, Currency.Cop), Date, isContribution: true, goalId);

    private static SavingsTransaction Withdrawal(decimal amount, Guid? goalId) =>
        new(Guid.NewGuid(), new Money(amount, Currency.Cop), Date, isContribution: false, goalId);

    // R6.1: transaction validation and shape

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Non_positive_movement_value_is_rejected(decimal amount)
    {
        Assert.Throws<NonPositiveAmountException>(() =>
            new SavingsTransaction(Guid.NewGuid(), new Money(amount, Currency.Cop), Date, true));
    }

    [Fact]
    public void General_fund_movement_has_null_goal()
    {
        var txn = Contribution(100_000m, goalId: null);
        Assert.Null(txn.GoalId);
        Assert.True(txn.IsContribution);
    }

    [Fact]
    public void Goal_target_must_be_positive()
    {
        Assert.Throws<NonPositiveAmountException>(() =>
            new SavingsGoal(Guid.NewGuid(), "Trip", new Money(0m, Currency.Cop)));
    }

    // R6.4: balance = contributions - withdrawals, scoped to the goal

    [Fact]
    public void Balance_sums_contributions_minus_withdrawals_for_the_goal()
    {
        var goal = new SavingsGoal(Guid.NewGuid(), "Trip", new Money(1_000_000m, Currency.Cop));
        var txns = new[]
        {
            Contribution(300_000m, goal.Id),
            Contribution(200_000m, goal.Id),
            Withdrawal(100_000m, goal.Id),
        };

        Assert.Equal(400_000m, goal.CurrentBalance(txns).Amount);
    }

    [Fact]
    public void Balance_ignores_other_goals_and_general_fund()
    {
        var goal = new SavingsGoal(Guid.NewGuid(), "Trip", new Money(1_000_000m, Currency.Cop));
        var otherGoalId = Guid.NewGuid();
        var txns = new[]
        {
            Contribution(300_000m, goal.Id),       // counts
            Contribution(500_000m, otherGoalId),   // ignored (other goal)
            Contribution(400_000m, goalId: null),  // ignored (general fund)
        };

        Assert.Equal(300_000m, goal.CurrentBalance(txns).Amount);
    }

    [Fact]
    public void Balance_is_zero_without_movements()
    {
        var goal = new SavingsGoal(Guid.NewGuid(), "Trip", new Money(1_000_000m, Currency.Cop));
        Assert.True(goal.CurrentBalance(Array.Empty<SavingsTransaction>()).IsZero);
    }

    // R6.5: progress percent

    [Fact]
    public void Progress_percent_is_balance_over_target()
    {
        var goal = new SavingsGoal(Guid.NewGuid(), "Trip", new Money(1_000_000m, Currency.Cop));
        var txns = new[] { Contribution(250_000m, goal.Id) };
        Assert.Equal(25m, goal.ProgressPercent(txns));
    }

    [Fact]
    public void Progress_percent_can_exceed_100_when_overfunded()
    {
        var goal = new SavingsGoal(Guid.NewGuid(), "Trip", new Money(1_000_000m, Currency.Cop));
        var txns = new[] { Contribution(1_200_000m, goal.Id) };
        Assert.Equal(120m, goal.ProgressPercent(txns));
    }

    [Fact]
    public void Progress_percent_is_zero_without_movements()
    {
        var goal = new SavingsGoal(Guid.NewGuid(), "Trip", new Money(1_000_000m, Currency.Cop));
        Assert.Equal(0m, goal.ProgressPercent(Array.Empty<SavingsTransaction>()));
    }
}
