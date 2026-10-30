using MiSaldo.Domain.Common;
using MiSaldo.Domain.Expenses;
using Xunit;

namespace MiSaldo.Domain.Tests.Expenses;

public class ExpenseTests
{
    private static readonly DateOnly Date = new(2026, 1, 10);

    private static Expense NewExpense(decimal budget = 500_000m) => new(
        Guid.NewGuid(),
        "Rent",
        Guid.NewGuid(),
        new Money(budget, Currency.Cop),
        Date,
        ExpenseType.Fixed);

    private static ExpensePayment Payment(decimal amount, int day = 10) => new(
        Guid.NewGuid(),
        new Money(amount, Currency.Cop),
        new DateOnly(2026, 1, day));

    // R4.3: validation of non-positive amounts

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Non_positive_budget_is_rejected(decimal budget)
    {
        Assert.Throws<NonPositiveAmountException>(() => NewExpense(budget));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Non_positive_payment_is_rejected(decimal amount)
    {
        Assert.Throws<NonPositiveAmountException>(() => Payment(amount));
    }

    // R4.2: derived status

    [Fact]
    public void Status_is_pending_when_nothing_paid()
    {
        var expense = NewExpense();
        Assert.Equal(ExpenseStatus.Pending, expense.Status());
        Assert.True(expense.TotalPaid().IsZero);
    }

    [Fact]
    public void Status_is_partial_when_paid_less_than_budget()
    {
        var expense = NewExpense(500_000m);
        expense.AddPayment(Payment(200_000m));
        Assert.Equal(ExpenseStatus.Partial, expense.Status());
        Assert.Equal(200_000m, expense.TotalPaid().Amount);
    }

    [Fact]
    public void Status_is_paid_when_paid_equals_budget()
    {
        var expense = NewExpense(500_000m);
        expense.AddPayment(Payment(300_000m));
        expense.AddPayment(Payment(200_000m));
        Assert.Equal(ExpenseStatus.Paid, expense.Status());
    }

    // R4.1 / R4.4: multiple payments and outstanding

    [Fact]
    public void Total_paid_sums_all_payments()
    {
        var expense = NewExpense(1_000_000m);
        expense.AddPayment(Payment(300_000m));
        expense.AddPayment(Payment(250_000m));
        Assert.Equal(550_000m, expense.TotalPaid().Amount);
        Assert.Equal(2, expense.Payments.Count);
    }

    [Fact]
    public void Outstanding_is_budget_minus_paid()
    {
        var expense = NewExpense(500_000m);
        expense.AddPayment(Payment(200_000m));
        Assert.Equal(300_000m, expense.Outstanding().Amount);
    }

    [Fact]
    public void Outstanding_is_zero_when_fully_paid()
    {
        var expense = NewExpense(500_000m);
        expense.AddPayment(Payment(500_000m));
        Assert.True(expense.Outstanding().IsZero);
    }

    // R4.5 (RN-M06): overpayment is valid and leaves outstanding at zero

    [Fact]
    public void Overpayment_is_allowed_and_leaves_outstanding_zero()
    {
        var expense = NewExpense(500_000m);
        expense.AddPayment(Payment(600_000m));
        Assert.Equal(ExpenseStatus.Paid, expense.Status());
        Assert.True(expense.Outstanding().IsZero);
        Assert.Equal(600_000m, expense.TotalPaid().Amount);
    }

    [Fact]
    public void Payment_in_a_different_currency_is_rejected()
    {
        var expense = NewExpense(500_000m); // COP
        var usdPayment = new ExpensePayment(Guid.NewGuid(), new Money(10m, Currency.Usd), Date);
        Assert.Throws<CurrencyMismatchException>(() => expense.AddPayment(usdPayment));
    }

    [Fact]
    public void Payments_are_exposed_as_read_only_list()
    {
        var expense = NewExpense();
        expense.AddPayment(Payment(100_000m));
        Assert.IsAssignableFrom<IReadOnlyList<ExpensePayment>>(expense.Payments);
        Assert.Single(expense.Payments);
    }
}
