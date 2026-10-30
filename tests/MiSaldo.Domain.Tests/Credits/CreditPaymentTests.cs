using MiSaldo.Domain.Common;
using MiSaldo.Domain.Credits;
using Xunit;

namespace MiSaldo.Domain.Tests.Credits;

public class CreditPaymentTests
{
    private static readonly DateOnly FirstDue = new(2026, 2, 1);

    private static Credit ThreeOf500k() => new(
        Guid.NewGuid(),
        "Laptop",
        new Money(1_500_000m, Currency.Cop),
        new Money(0m, Currency.Cop),
        3,
        FirstDue,
        PaymentFrequency.Monthly);

    private static CreditPayment Pay(decimal amount, int day = 5) =>
        new(Guid.NewGuid(), new Money(amount, Currency.Cop), new DateOnly(2026, 2, day));

    // --- Task 8.2: payment and recalculation ---

    // CP-05: pay 600,000 on installment 1 -> balance 900,000, future installments 450,000
    [Fact]
    public void Cp05_overpaying_first_installment_lowers_future_installments()
    {
        var credit = ThreeOf500k();
        credit.RegisterPayment(Pay(600_000m));

        Assert.Equal(900_000m, credit.OutstandingBalance().Amount);

        var open = credit.Installments.Where(i => i.IsOpen).ToList();
        Assert.Equal(2, open.Count);
        Assert.All(open, i => Assert.Equal(450_000m, i.Amount.Amount));
    }

    // CP-06: pay 400,000 on installment 1 -> balance 1,100,000, future installments 550,000
    [Fact]
    public void Cp06_underpaying_first_installment_raises_future_installments()
    {
        var credit = ThreeOf500k();
        credit.RegisterPayment(Pay(400_000m));

        Assert.Equal(1_100_000m, credit.OutstandingBalance().Amount);

        var open = credit.Installments.Where(i => i.IsOpen).ToList();
        Assert.Equal(2, open.Count);
        Assert.All(open, i => Assert.Equal(550_000m, i.Amount.Amount));
    }

    [Fact]
    public void Balance_is_original_minus_payments()
    {
        var credit = ThreeOf500k();
        credit.RegisterPayment(Pay(500_000m));
        credit.RegisterPayment(Pay(300_000m, day: 10));
        Assert.Equal(700_000m, credit.OutstandingBalance().Amount);
    }

    [Fact]
    public void Registered_payments_are_kept_as_history()
    {
        var credit = ThreeOf500k();
        credit.RegisterPayment(Pay(600_000m));
        credit.RegisterPayment(Pay(200_000m, day: 10));
        Assert.Equal(2, credit.Payments.Count);
        Assert.Equal(600_000m, credit.Payments[0].Value.Amount);
        Assert.Equal(200_000m, credit.Payments[1].Value.Amount);
    }

    [Fact]
    public void Payment_in_a_different_currency_is_rejected()
    {
        var credit = ThreeOf500k();
        var usd = new CreditPayment(Guid.NewGuid(), new Money(10m, Currency.Usd), FirstDue);
        Assert.Throws<CurrencyMismatchException>(() => credit.RegisterPayment(usd));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Non_positive_payment_is_rejected(decimal amount)
    {
        var credit = ThreeOf500k();
        Assert.Throws<NonPositiveAmountException>(() => credit.RegisterPayment(Pay(amount)));
    }

    // --- Task 8.3: closure and edge rules ---

    // RF-CRD-09: balance reaches zero -> Paid, open installments cancelled
    [Fact]
    public void Paying_full_balance_marks_credit_paid_and_cancels_open_installments()
    {
        var credit = ThreeOf500k();
        credit.RegisterPayment(Pay(1_500_000m));

        Assert.Equal(CreditStatus.Paid, credit.Status);
        Assert.True(credit.OutstandingBalance().IsZero);
        Assert.DoesNotContain(credit.Installments, i => i.IsOpen);
    }

    // RF-CRD-08: a partial payment does not mark the credit paid
    [Fact]
    public void Partial_payment_keeps_credit_active()
    {
        var credit = ThreeOf500k();
        credit.RegisterPayment(Pay(500_000m));
        Assert.Equal(CreditStatus.Active, credit.Status);
    }

    // RN-C05: a payment cannot exceed the outstanding balance
    [Fact]
    public void Payment_greater_than_balance_is_rejected()
    {
        var credit = ThreeOf500k();
        var ex = Assert.Throws<PaymentExceedsBalanceException>(() =>
            credit.RegisterPayment(Pay(1_600_000m)));
        Assert.Equal(1_600_000m, ex.Payment.Amount);
        Assert.Equal(1_500_000m, ex.OutstandingBalance.Amount);
    }

    // RN-C04: underpaying the last installment adds a catch-up installment one period later
    [Fact]
    public void Underpaying_the_last_installment_adds_a_catch_up_installment()
    {
        var credit = new Credit(
            Guid.NewGuid(),
            "Phone",
            new Money(500_000m, Currency.Cop),
            new Money(0m, Currency.Cop),
            1,
            FirstDue,
            PaymentFrequency.Monthly);

        credit.RegisterPayment(Pay(300_000m));

        Assert.Equal(CreditStatus.Active, credit.Status);
        Assert.Equal(200_000m, credit.OutstandingBalance().Amount);
        Assert.Equal(2, credit.Installments.Count);

        var catchUp = credit.Installments[1];
        Assert.True(catchUp.IsOpen);
        Assert.Equal(200_000m, catchUp.Amount.Amount);
        Assert.Equal(new DateOnly(2026, 3, 1), catchUp.DueDate); // one month after the last due
    }

    // RF-CRD-11: no negative balance even if the schedule is fully settled
    [Fact]
    public void Balance_never_goes_negative()
    {
        var credit = ThreeOf500k();
        credit.RegisterPayment(Pay(1_500_000m));
        Assert.False(credit.OutstandingBalance().IsNegative);
    }
}
