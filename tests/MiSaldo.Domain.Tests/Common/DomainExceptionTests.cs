using MiSaldo.Domain.Common;
using Xunit;

namespace MiSaldo.Domain.Tests.Common;

public class DomainExceptionTests
{
    public static IEnumerable<object[]> AllDomainExceptions()
    {
        yield return new object[] { new CurrencyMismatchException(Currency.Cop, Currency.Usd) };
        yield return new object[] { new NonPositiveAmountException("Income value", -5m) };
        yield return new object[]
        {
            new PaymentExceedsBalanceException(
                new Money(1_000m, Currency.Cop), new Money(500m, Currency.Cop)),
        };
        yield return new object[]
        {
            new WithdrawalExceedsSavingsException(
                new Money(300m, Currency.Cop), new Money(200m, Currency.Cop)),
        };
        yield return new object[]
        {
            new SavingsExceedsAvailableException(
                new Money(300m, Currency.Cop), new Money(200m, Currency.Cop)),
        };
        yield return new object[]
        {
            new InvalidPeriodRangeException(
                new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 1)),
        };
    }

    [Theory]
    [MemberData(nameof(AllDomainExceptions))]
    public void Every_subtype_derives_from_DomainException(DomainException exception)
    {
        Assert.IsAssignableFrom<DomainException>(exception);
    }

    [Theory]
    [MemberData(nameof(AllDomainExceptions))]
    public void Every_subtype_preserves_a_non_empty_message(DomainException exception)
    {
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Fact]
    public void NonPositiveAmount_keeps_context()
    {
        var ex = new NonPositiveAmountException("Payment value", -1m);
        Assert.Equal("Payment value", ex.What);
        Assert.Equal(-1m, ex.Amount);
        Assert.Contains("Payment value", ex.Message);
    }

    [Fact]
    public void PaymentExceedsBalance_keeps_amounts()
    {
        var payment = new Money(1_000m, Currency.Cop);
        var balance = new Money(500m, Currency.Cop);
        var ex = new PaymentExceedsBalanceException(payment, balance);
        Assert.Equal(payment, ex.Payment);
        Assert.Equal(balance, ex.OutstandingBalance);
    }

    [Fact]
    public void InvalidPeriodRange_keeps_dates()
    {
        var start = new DateOnly(2026, 1, 31);
        var end = new DateOnly(2026, 1, 1);
        var ex = new InvalidPeriodRangeException(start, end);
        Assert.Equal(start, ex.StartDate);
        Assert.Equal(end, ex.EndDate);
    }
}
