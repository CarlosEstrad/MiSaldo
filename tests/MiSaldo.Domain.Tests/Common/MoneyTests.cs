using MiSaldo.Domain.Common;
using Xunit;

namespace MiSaldo.Domain.Tests.Common;

public class MoneyTests
{
    // --- Task 2.1: decimal places and banker's rounding ---

    [Fact]
    public void Cop_rounds_to_zero_decimals_half_to_even()
    {
        // 1500000.50 -> 1500000 (0.5 rounds to the even integer)
        var money = new Money(1_500_000.50m, Currency.Cop);
        Assert.Equal(1_500_000m, money.Round().Amount);
    }

    [Fact]
    public void Cop_rounds_half_up_to_even_when_odd()
    {
        // 1500001.50 -> 1500002 (rounds to even)
        var money = new Money(1_500_001.50m, Currency.Cop);
        Assert.Equal(1_500_002m, money.Round().Amount);
    }

    [Fact]
    public void Usd_keeps_two_decimals()
    {
        var money = new Money(10.005m, Currency.Usd);
        // 10.005 -> 10.00 (half to even: 0 is even)
        Assert.Equal(10.00m, money.Round().Amount);
    }

    [Fact]
    public void Usd_rounds_half_to_even_upward_when_odd()
    {
        var money = new Money(10.015m, Currency.Usd);
        // 10.015 -> 10.02 (rounds to even)
        Assert.Equal(10.02m, money.Round().Amount);
    }

    [Fact]
    public void Zero_creates_zero_amount_in_currency()
    {
        var zero = Money.Zero(Currency.Cop);
        Assert.True(zero.IsZero);
        Assert.Equal(Currency.Cop, zero.Currency);
    }

    [Theory]
    [InlineData(100, true, false, false)]
    [InlineData(-100, false, true, false)]
    [InlineData(0, false, false, true)]
    public void Sign_flags_reflect_amount(decimal amount, bool positive, bool negative, bool zero)
    {
        var money = new Money(amount, Currency.Cop);
        Assert.Equal(positive, money.IsPositive);
        Assert.Equal(negative, money.IsNegative);
        Assert.Equal(zero, money.IsZero);
    }

    // --- Task 2.2: arithmetic, operators, currency guard ---

    [Fact]
    public void Add_same_currency_sums_amounts()
    {
        var a = new Money(1_000m, Currency.Cop);
        var b = new Money(500m, Currency.Cop);
        Assert.Equal(1_500m, (a + b).Amount);
    }

    [Fact]
    public void Subtract_same_currency_subtracts_amounts()
    {
        var a = new Money(1_000m, Currency.Cop);
        var b = new Money(300m, Currency.Cop);
        Assert.Equal(700m, (a - b).Amount);
    }

    [Fact]
    public void Subtract_can_go_negative()
    {
        var a = new Money(100m, Currency.Cop);
        var b = new Money(300m, Currency.Cop);
        Assert.Equal(-200m, (a - b).Amount);
    }

    [Fact]
    public void Add_different_currencies_throws()
    {
        var cop = new Money(1_000m, Currency.Cop);
        var usd = new Money(10m, Currency.Usd);
        var ex = Assert.Throws<CurrencyMismatchException>(() => cop + usd);
        Assert.Equal(Currency.Cop, ex.Left);
        Assert.Equal(Currency.Usd, ex.Right);
    }

    [Fact]
    public void Subtract_different_currencies_throws()
    {
        var cop = new Money(1_000m, Currency.Cop);
        var usd = new Money(10m, Currency.Usd);
        Assert.Throws<CurrencyMismatchException>(() => cop - usd);
    }

    [Fact]
    public void Comparing_different_currencies_throws()
    {
        var cop = new Money(1_000m, Currency.Cop);
        var usd = new Money(10m, Currency.Usd);
        Assert.Throws<CurrencyMismatchException>(() => cop > usd);
    }

    [Fact]
    public void Comparison_operators_compare_same_currency()
    {
        var small = new Money(100m, Currency.Cop);
        var big = new Money(200m, Currency.Cop);
        Assert.True(small < big);
        Assert.True(big > small);
        Assert.True(small <= new Money(100m, Currency.Cop));
        Assert.True(big >= new Money(200m, Currency.Cop));
    }

    [Fact]
    public void Value_equality_holds_for_same_amount_and_currency()
    {
        Assert.Equal(new Money(100m, Currency.Cop), new Money(100m, Currency.Cop));
        Assert.NotEqual(new Money(100m, Currency.Cop), new Money(100m, Currency.Usd));
    }

    // --- Task 2.3: accumulate then round loses/creates no money ---

    [Fact]
    public void Summing_then_rounding_matches_exact_sum_cop()
    {
        // Three COP amounts with fractions that individually would round,
        // but the exact total is a whole number: no money created or lost.
        var parts = new[]
        {
            new Money(0.4m, Currency.Cop),
            new Money(0.4m, Currency.Cop),
            new Money(0.2m, Currency.Cop),
        };

        var total = parts.Aggregate(Money.Zero(Currency.Cop), (acc, m) => acc + m);

        Assert.Equal(1m, total.Amount);          // exact accumulation
        Assert.Equal(1m, total.Round().Amount);  // rounding the total keeps it whole
    }

    [Fact]
    public void Rounding_each_part_would_lose_money_but_total_does_not()
    {
        // If we rounded each 0.4 COP to 0 we'd report 0; accumulating first
        // and rounding the total reports 1 -- the correct, money-preserving path.
        var roundedEach = new[]
        {
            new Money(0.4m, Currency.Cop).Round(),
            new Money(0.4m, Currency.Cop).Round(),
            new Money(0.2m, Currency.Cop).Round(),
        }.Aggregate(Money.Zero(Currency.Cop), (acc, m) => acc + m);

        var accumulatedThenRounded = new[]
        {
            new Money(0.4m, Currency.Cop),
            new Money(0.4m, Currency.Cop),
            new Money(0.2m, Currency.Cop),
        }.Aggregate(Money.Zero(Currency.Cop), (acc, m) => acc + m).Round();

        Assert.Equal(0m, roundedEach.Amount);
        Assert.Equal(1m, accumulatedThenRounded.Amount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(100)]
    public void Repeated_addition_preserves_exact_total(int count)
    {
        var unit = new Money(0.01m, Currency.Usd);
        var total = Enumerable.Range(0, count)
            .Aggregate(Money.Zero(Currency.Usd), (acc, _) => acc + unit);

        Assert.Equal(0.01m * count, total.Amount);
        Assert.Equal(Math.Round(0.01m * count, 2, MidpointRounding.ToEven), total.Round().Amount);
    }
}
