using MiSaldo.Domain.Common;
using MiSaldo.Domain.Credits;
using MiSaldo.Domain.Engine;
using MiSaldo.Domain.Expenses;
using MiSaldo.Domain.Incomes;
using MiSaldo.Domain.Periods;
using MiSaldo.Domain.Savings;
using Xunit;

namespace MiSaldo.Domain.Tests.Engine;

public class FinancialEngineTests
{
    private static readonly Currency Cop = Currency.Cop;

    private static FinancialPeriod Period => new(
        Guid.NewGuid(),
        PeriodType.Monthly,
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 1, 31),
        carryOverEnabled: true);

    private static Money Cop1(decimal amount) => new(amount, Cop);

    private static readonly DateOnly InPeriod = new(2026, 1, 15);

    private static IncomeTransaction Received(decimal amount, DateOnly? on = null) => new(
        Guid.NewGuid(), "Salary", Cop1(amount), InPeriod, "Employer",
        IncomeStatus.Received, on ?? InPeriod);

    private static IncomeTransaction Planned(decimal amount) => new(
        Guid.NewGuid(), "Bonus", Cop1(amount), InPeriod, "Employer", IncomeStatus.Planned);

    // An expense fully paid within the period (no outstanding).
    private static Expense PaidExpense(decimal amount)
    {
        var e = new Expense(Guid.NewGuid(), "Groceries", Guid.NewGuid(), Cop1(amount), InPeriod, ExpenseType.Variable);
        e.AddPayment(new ExpensePayment(Guid.NewGuid(), Cop1(amount), InPeriod));
        return e;
    }

    // An unpaid expense -> full outstanding obligation.
    private static Expense UnpaidExpense(decimal amount) =>
        new(Guid.NewGuid(), "Utilities", Guid.NewGuid(), Cop1(amount), InPeriod, ExpenseType.Fixed);

    // --- Task 9.1 ---

    // CP-01: received 2,000,000; paid 500,000 -> cash 1,500,000
    [Fact]
    public void Cp01_cash_is_received_minus_paid()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(2_000_000m)],
            Expenses = [PaidExpense(500_000m)],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(2_000_000m, r.ReceivedIncome.Amount);
        Assert.Equal(500_000m, r.PaidExpense.Amount);
        Assert.Equal(1_500_000m, r.CashOnHand.Amount);
    }

    // CP-02: received 2,000,000; paid 500,000; outstanding 300,000 -> cash 1,500,000; free 1,200,000
    [Fact]
    public void Cp02_free_money_is_cash_minus_reserved()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(2_000_000m)],
            Expenses = [PaidExpense(500_000m), UnpaidExpense(300_000m)],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(1_500_000m, r.CashOnHand.Amount);
        Assert.Equal(300_000m, r.OutstandingObligations.Amount);
        Assert.Equal(1_200_000m, r.FreeMoney.Amount);
    }

    // CP-03: received 2,000,000; savings contribution 200,000 -> cash 1,800,000; savings +200,000; expense 0
    [Fact]
    public void Cp03_savings_contribution_lowers_cash_but_is_not_expense()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(2_000_000m)],
            Savings =
            [
                new SavingsTransaction(Guid.NewGuid(), Cop1(200_000m), InPeriod, isContribution: true),
            ],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(1_800_000m, r.CashOnHand.Amount);
        Assert.Equal(200_000m, r.SavingsContributions.Amount);
        Assert.True(r.PaidExpense.IsZero);
    }

    // CP-10: received 2,500,000; paid 800k+300k+100k; savings 200k; outstanding 400k -> cash 1,100,000; free 700,000
    [Fact]
    public void Cp10_section3_example()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(2_500_000m)],
            Expenses =
            [
                PaidExpense(800_000m),
                PaidExpense(300_000m),
                PaidExpense(100_000m),
                UnpaidExpense(400_000m),
            ],
            Savings =
            [
                new SavingsTransaction(Guid.NewGuid(), Cop1(200_000m), InPeriod, isContribution: true),
            ],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(1_100_000m, r.CashOnHand.Amount);
        Assert.Equal(700_000m, r.FreeMoney.Amount);
        Assert.Equal(200_000m, r.SavingsContributions.Amount);
    }

    // CP-13: a planned income does not affect cash
    [Fact]
    public void Cp13_planned_income_does_not_affect_cash()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(2_000_000m), Planned(1_000_000m)],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(2_000_000m, r.ReceivedIncome.Amount);
        Assert.Equal(2_000_000m, r.CashOnHand.Amount);
    }

    [Fact]
    public void Income_received_outside_the_period_is_excluded()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(2_000_000m, on: new DateOnly(2026, 2, 5))],
        };

        var r = FinancialEngine.Compute(input);
        Assert.True(r.ReceivedIncome.IsZero);
    }

    [Fact]
    public void Carry_over_is_part_of_cash()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            CarryOver = Cop1(500_000m),
            Incomes = [Received(1_000_000m)],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(500_000m, r.CarryOver.Amount);
        Assert.Equal(1_500_000m, r.CashOnHand.Amount);
    }

    [Fact]
    public void Free_money_can_be_negative_and_is_shown_as_is()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(500_000m)],
            Expenses = [UnpaidExpense(900_000m)],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(500_000m, r.CashOnHand.Amount);
        Assert.Equal(900_000m, r.Reserved.Amount);
        Assert.Equal(-400_000m, r.FreeMoney.Amount);
        Assert.True(r.FreeMoney.IsNegative);
    }

    [Fact]
    public void Budget_consumption_is_paid_over_income_plus_carry_over()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(1_000_000m)],
            Expenses = [PaidExpense(800_000m)],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(0.8m, r.BudgetConsumption);
    }

    [Fact]
    public void Budget_consumption_is_zero_when_no_income_or_carry_over()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Expenses = [PaidExpense(100_000m)],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(0m, r.BudgetConsumption);
    }

    // --- Task 9.2: credit payment counted once as expense (CP-17) ---

    // CP-17: received 2,000,000; credit installment payment 500,000 -> cash 1,500,000;
    // paid expense 500,000; exposed as credit payment, counted once.
    [Fact]
    public void Cp17_credit_payment_counts_as_expense_once()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(2_000_000m)],
            CreditPayments =
            [
                new CreditPayment(Guid.NewGuid(), Cop1(500_000m), InPeriod),
            ],
        };

        var r = FinancialEngine.Compute(input);
        Assert.Equal(1_500_000m, r.CashOnHand.Amount);
        Assert.Equal(500_000m, r.PaidExpense.Amount);
        Assert.Equal(500_000m, r.CreditPayments.Amount);
    }

    [Fact]
    public void Credit_payment_is_not_double_counted_with_expense_payments()
    {
        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(2_000_000m)],
            Expenses = [PaidExpense(300_000m)],
            CreditPayments =
            [
                new CreditPayment(Guid.NewGuid(), Cop1(500_000m), InPeriod),
            ],
        };

        var r = FinancialEngine.Compute(input);
        // paid = 300,000 expense + 500,000 credit = 800,000 (credit counted once)
        Assert.Equal(800_000m, r.PaidExpense.Amount);
        Assert.Equal(500_000m, r.CreditPayments.Amount);
        Assert.Equal(1_200_000m, r.CashOnHand.Amount);
    }

    [Fact]
    public void Open_credit_installment_due_in_period_counts_as_reserved()
    {
        // Build a credit with an installment due inside the period.
        var credit = new Credit(
            Guid.NewGuid(), "TV", Cop1(900_000m), Cop1(0m), 3,
            new DateOnly(2026, 1, 20), PaymentFrequency.Monthly);

        var input = new EngineInput
        {
            Period = Period,
            Currency = Cop,
            Incomes = [Received(1_000_000m)],
            CreditInstallments = credit.Installments,
        };

        var r = FinancialEngine.Compute(input);
        // only the installment due 2026-01-20 (300,000) is within the period
        Assert.Equal(300_000m, r.Reserved.Amount);
        Assert.Equal(700_000m, r.FreeMoney.Amount);
    }
}
