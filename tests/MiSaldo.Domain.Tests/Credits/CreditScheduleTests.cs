using MiSaldo.Domain.Common;
using MiSaldo.Domain.Credits;
using Xunit;

namespace MiSaldo.Domain.Tests.Credits;

public class CreditScheduleTests
{
    private static readonly DateOnly FirstDue = new(2026, 2, 1);

    private static Credit NewCredit(
        decimal original, int count, decimal down = 0m) => new(
        Guid.NewGuid(),
        "Laptop",
        new Money(original, Currency.Cop),
        new Money(down, Currency.Cop),
        count,
        FirstDue,
        PaymentFrequency.Monthly);

    // CP-04: 1,500,000 in 3 installments -> 3 x 500,000
    [Fact]
    public void Cp04_schedule_is_three_equal_installments()
    {
        var credit = NewCredit(1_500_000m, 3);
        Assert.Equal(3, credit.Installments.Count);
        Assert.All(credit.Installments, i => Assert.Equal(500_000m, i.Amount.Amount));
        Assert.Equal(1_500_000m, credit.OutstandingBalance().Amount);
    }

    // CP-15: 1,000,000 in 3 installments -> 333,333 / 333,333 / 333,334 (sums exactly)
    [Fact]
    public void Cp15_last_installment_absorbs_rounding_residue()
    {
        var credit = NewCredit(1_000_000m, 3);
        Assert.Equal(333_333m, credit.Installments[0].Amount.Amount);
        Assert.Equal(333_333m, credit.Installments[1].Amount.Amount);
        Assert.Equal(333_334m, credit.Installments[2].Amount.Amount);

        var sum = credit.Installments.Sum(i => i.Amount.Amount);
        Assert.Equal(1_000_000m, sum);
    }

    [Fact]
    public void Schedule_sum_equals_financed_for_various_counts()
    {
        foreach (var count in new[] { 1, 2, 3, 6, 7, 12, 13 })
        {
            var credit = NewCredit(1_000_000m, count);
            var sum = credit.Installments.Sum(i => i.Amount.Amount);
            Assert.Equal(1_000_000m, sum);
            Assert.Equal(count, credit.Installments.Count);
        }
    }

    [Fact]
    public void Down_payment_reduces_financed_amount()
    {
        var credit = NewCredit(1_200_000m, 3, down: 300_000m);
        Assert.Equal(900_000m, credit.Financed.Amount);
        Assert.Equal(900_000m, credit.OutstandingBalance().Amount);
        Assert.All(credit.Installments, i => Assert.Equal(300_000m, i.Amount.Amount));
    }

    [Fact]
    public void Due_dates_follow_monthly_frequency()
    {
        var credit = NewCredit(900_000m, 3);
        Assert.Equal(new DateOnly(2026, 2, 1), credit.Installments[0].DueDate);
        Assert.Equal(new DateOnly(2026, 3, 1), credit.Installments[1].DueDate);
        Assert.Equal(new DateOnly(2026, 4, 1), credit.Installments[2].DueDate);
    }

    // RF-CRD-04: original value stays separate from the outstanding balance
    [Fact]
    public void Original_value_is_separate_from_balance()
    {
        var credit = NewCredit(1_500_000m, 3);
        Assert.Equal(1_500_000m, credit.OriginalValue.Amount);
        Assert.Equal(1_500_000m, credit.OutstandingBalance().Amount);
    }

    // Validation

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Non_positive_original_value_is_rejected(decimal original)
    {
        Assert.Throws<NonPositiveAmountException>(() => NewCredit(original, 3));
    }

    [Fact]
    public void Zero_installments_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewCredit(1_000_000m, 0));
    }

    [Fact]
    public void Down_payment_greater_than_original_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => NewCredit(1_000_000m, 3, down: 1_200_000m));
    }

    [Fact]
    public void All_installments_start_open()
    {
        var credit = NewCredit(900_000m, 3);
        Assert.All(credit.Installments, i => Assert.True(i.IsOpen));
        Assert.Equal(CreditStatus.Active, credit.Status);
    }
}
