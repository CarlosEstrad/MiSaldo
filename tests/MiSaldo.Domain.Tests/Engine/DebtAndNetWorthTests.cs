using MiSaldo.Domain.Common;
using MiSaldo.Domain.Credits;
using MiSaldo.Domain.Engine;
using MiSaldo.Domain.Expenses;
using Xunit;

namespace MiSaldo.Domain.Tests.Engine;

public class DebtAndNetWorthTests
{
    private static readonly Currency Cop = Currency.Cop;
    private static readonly DateOnly Today = new(2026, 2, 1);

    private static Money Cop1(decimal amount) => new(amount, Cop);

    private static Credit ActiveCredit(decimal original, int count)
    {
        return new Credit(
            Guid.NewGuid(), "TV", Cop1(original), Cop1(0m), count,
            new DateOnly(2026, 3, 1), PaymentFrequency.Monthly);
    }

    private static Expense ExpenseOn(DateOnly date, decimal budget, decimal paid = 0m)
    {
        var e = new Expense(Guid.NewGuid(), "Bill", Guid.NewGuid(), Cop1(budget), date, ExpenseType.Fixed);
        if (paid > 0m)
        {
            e.AddPayment(new ExpensePayment(Guid.NewGuid(), Cop1(paid), date));
        }

        return e;
    }

    // --- R9.1: pending debt (RN-F11, D-04) ---

    [Fact]
    public void Pending_debt_sums_active_credit_balances()
    {
        var credits = new[] { ActiveCredit(900_000m, 3), ActiveCredit(600_000m, 2) };
        var debt = FinancialEngine.PendingDebt(Cop, credits, [], Today);
        Assert.Equal(1_500_000m, debt.Amount);
    }

    [Fact]
    public void Overdue_unpaid_obligation_counts_as_debt()
    {
        // Expense dated in January, unpaid; today is Feb 1 -> overdue.
        var expenses = new[] { ExpenseOn(new DateOnly(2026, 1, 10), 400_000m) };
        var debt = FinancialEngine.PendingDebt(Cop, [], expenses, Today);
        Assert.Equal(400_000m, debt.Amount);
    }

    [Fact]
    public void Future_obligation_is_not_debt()
    {
        // Expense dated in March, unpaid; today is Feb 1 -> future, not debt.
        var expenses = new[] { ExpenseOn(new DateOnly(2026, 3, 10), 400_000m) };
        var debt = FinancialEngine.PendingDebt(Cop, [], expenses, Today);
        Assert.True(debt.IsZero);
    }

    [Fact]
    public void Overdue_but_fully_paid_obligation_is_not_debt()
    {
        var expenses = new[] { ExpenseOn(new DateOnly(2026, 1, 10), 400_000m, paid: 400_000m) };
        var debt = FinancialEngine.PendingDebt(Cop, [], expenses, Today);
        Assert.True(debt.IsZero);
    }

    [Fact]
    public void Paid_credit_is_not_counted_in_debt()
    {
        var credit = ActiveCredit(500_000m, 1);
        credit.RegisterPayment(new CreditPayment(Guid.NewGuid(), Cop1(500_000m), new DateOnly(2026, 3, 1)));
        Assert.Equal(CreditStatus.Paid, credit.Status);

        var debt = FinancialEngine.PendingDebt(Cop, [credit], [], Today);
        Assert.True(debt.IsZero);
    }

    [Fact]
    public void Debt_combines_credits_and_overdue_obligations()
    {
        var credits = new[] { ActiveCredit(900_000m, 3) };
        var expenses = new[]
        {
            ExpenseOn(new DateOnly(2026, 1, 10), 400_000m, paid: 100_000m), // overdue, 300,000 outstanding
            ExpenseOn(new DateOnly(2026, 3, 10), 500_000m),                 // future, ignored
        };

        var debt = FinancialEngine.PendingDebt(Cop, credits, expenses, Today);
        Assert.Equal(1_200_000m, debt.Amount); // 900,000 + 300,000
    }

    // --- R9.2: net worth (RN-F12) ---

    [Fact]
    public void Net_worth_is_cash_plus_savings_minus_debt()
    {
        var netWorth = FinancialEngine.NetWorth(
            accumulatedCash: Cop1(2_000_000m),
            accumulatedSavings: Cop1(500_000m),
            pendingDebt: Cop1(800_000m));

        Assert.Equal(1_700_000m, netWorth.Amount);
    }

    [Fact]
    public void Net_worth_can_be_negative_when_debt_exceeds_assets()
    {
        var netWorth = FinancialEngine.NetWorth(
            accumulatedCash: Cop1(300_000m),
            accumulatedSavings: Cop1(200_000m),
            pendingDebt: Cop1(1_000_000m));

        Assert.Equal(-500_000m, netWorth.Amount);
        Assert.True(netWorth.IsNegative);
    }
}
