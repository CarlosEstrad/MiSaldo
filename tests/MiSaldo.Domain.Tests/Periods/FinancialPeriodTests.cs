using MiSaldo.Domain.Common;
using MiSaldo.Domain.Periods;
using Xunit;

namespace MiSaldo.Domain.Tests.Periods;

public class FinancialPeriodTests
{
    private static FinancialPeriod MonthlyJan2026(bool carryOver = true) => new(
        Guid.NewGuid(),
        PeriodType.Monthly,
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 1, 31),
        carryOver);

    // --- Task 4.1: status, contains, range validation, close ---

    [Fact]
    public void End_before_start_is_rejected()
    {
        var start = new DateOnly(2026, 1, 31);
        var end = new DateOnly(2026, 1, 1);
        var ex = Assert.Throws<InvalidPeriodRangeException>(() =>
            new FinancialPeriod(Guid.NewGuid(), PeriodType.Monthly, start, end, true));
        Assert.Equal(start, ex.StartDate);
        Assert.Equal(end, ex.EndDate);
    }

    [Fact]
    public void Single_day_period_is_allowed()
    {
        var day = new DateOnly(2026, 1, 1);
        var period = new FinancialPeriod(Guid.NewGuid(), PeriodType.Weekly, day, day, true);
        Assert.True(period.Contains(day));
    }

    [Theory]
    [InlineData(2025, 12, 31, false)] // before start
    [InlineData(2026, 1, 1, true)]    // start boundary
    [InlineData(2026, 1, 15, true)]   // inside
    [InlineData(2026, 1, 31, true)]   // end boundary
    [InlineData(2026, 2, 1, false)]   // after end
    public void Contains_is_inclusive_of_both_ends(int y, int m, int d, bool expected)
    {
        var period = MonthlyJan2026();
        Assert.Equal(expected, period.Contains(new DateOnly(y, m, d)));
    }

    [Fact]
    public void Status_is_planned_before_start()
    {
        Assert.Equal(PeriodStatus.Planned, MonthlyJan2026().StatusOn(new DateOnly(2025, 12, 20)));
    }

    [Fact]
    public void Status_is_active_within_range()
    {
        Assert.Equal(PeriodStatus.Active, MonthlyJan2026().StatusOn(new DateOnly(2026, 1, 10)));
    }

    [Fact]
    public void Status_is_overdue_after_end_when_not_closed()
    {
        Assert.Equal(PeriodStatus.Overdue, MonthlyJan2026().StatusOn(new DateOnly(2026, 2, 5)));
    }

    [Fact]
    public void Status_is_closed_after_close_regardless_of_date()
    {
        var period = MonthlyJan2026();
        period.Close();
        Assert.True(period.IsClosed);
        Assert.Equal(PeriodStatus.Closed, period.StatusOn(new DateOnly(2026, 1, 10)));
        Assert.Equal(PeriodStatus.Closed, period.StatusOn(new DateOnly(2026, 2, 5)));
    }

    // --- Task 4.3: carry-over exposure (RN-P05, D-03) ---

    [Fact]
    public void Positive_balance_with_carry_over_enabled_carries_over()
    {
        var period = MonthlyJan2026(carryOver: true);
        var balance = new Money(500_000m, Currency.Cop);
        Assert.Equal(balance, period.CarryOverFor(balance));
    }

    [Fact]
    public void Negative_balance_does_not_carry_over()
    {
        var period = MonthlyJan2026(carryOver: true);
        var balance = new Money(-200_000m, Currency.Cop);
        Assert.Equal(Money.Zero(Currency.Cop), period.CarryOverFor(balance));
    }

    [Fact]
    public void Zero_balance_does_not_carry_over()
    {
        var period = MonthlyJan2026(carryOver: true);
        Assert.Equal(Money.Zero(Currency.Cop), period.CarryOverFor(Money.Zero(Currency.Cop)));
    }

    [Fact]
    public void Positive_balance_with_carry_over_disabled_does_not_carry_over()
    {
        var period = MonthlyJan2026(carryOver: false);
        var balance = new Money(500_000m, Currency.Cop);
        Assert.Equal(Money.Zero(Currency.Cop), period.CarryOverFor(balance));
    }
}
