using MiSaldo.Domain.Common;
using MiSaldo.Domain.Incomes;
using Xunit;

namespace MiSaldo.Domain.Tests.Incomes;

public class IncomeTransactionTests
{
    private static readonly DateOnly Date = new(2026, 1, 10);

    private static IncomeTransaction Planned(decimal amount = 2_000_000m) => new(
        Guid.NewGuid(),
        "Salary",
        new Money(amount, Currency.Cop),
        Date,
        "Employer",
        IncomeStatus.Planned);

    // R3.1 / R3.2: creation and value validation

    [Fact]
    public void Planned_income_starts_planned_without_received_date()
    {
        var income = Planned();
        Assert.Equal(IncomeStatus.Planned, income.Status);
        Assert.False(income.IsReceived);
        Assert.Null(income.ReceivedDate);
    }

    [Fact]
    public void Received_income_requires_a_reception_date()
    {
        var received = new IncomeTransaction(
            Guid.NewGuid(),
            "Salary",
            new Money(2_000_000m, Currency.Cop),
            Date,
            "Employer",
            IncomeStatus.Received,
            new DateOnly(2026, 1, 12));

        Assert.True(received.IsReceived);
        Assert.Equal(new DateOnly(2026, 1, 12), received.ReceivedDate);
    }

    [Fact]
    public void Received_status_without_reception_date_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new IncomeTransaction(
            Guid.NewGuid(),
            "Salary",
            new Money(2_000_000m, Currency.Cop),
            Date,
            "Employer",
            IncomeStatus.Received));
    }

    [Fact]
    public void Planned_status_with_reception_date_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new IncomeTransaction(
            Guid.NewGuid(),
            "Salary",
            new Money(2_000_000m, Currency.Cop),
            Date,
            "Employer",
            IncomeStatus.Planned,
            new DateOnly(2026, 1, 12)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2_000_000)]
    public void Non_positive_value_is_rejected(decimal amount)
    {
        var ex = Assert.Throws<NonPositiveAmountException>(() => new IncomeTransaction(
            Guid.NewGuid(),
            "Salary",
            new Money(amount, Currency.Cop),
            Date,
            "Employer",
            IncomeStatus.Planned));
        Assert.Equal(amount, ex.Amount);
    }

    // R3.3: marking received sets the date

    [Fact]
    public void Mark_received_sets_status_and_date()
    {
        var income = Planned();
        var received = new DateOnly(2026, 1, 15);

        income.MarkReceived(received);

        Assert.Equal(IncomeStatus.Received, income.Status);
        Assert.True(income.IsReceived);
        Assert.Equal(received, income.ReceivedDate);
    }

    // R3.4 (CP-13): a planned income is not received, so the engine won't count it
    [Fact]
    public void Planned_income_is_not_flagged_as_received()
    {
        Assert.False(Planned().IsReceived);
    }
}
