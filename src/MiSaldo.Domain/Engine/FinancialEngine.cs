using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Engine;

/// <summary>
/// The single source of truth for period indicators. It is stateless: the same
/// input always yields the same output, so any change in the movements is
/// reflected immediately and no client ever recomputes a balance (RNF-DAT-07,
/// RF-PRD-08). All filtering is by movement date against the period range
/// (RN-M02).
/// </summary>
public static class FinancialEngine
{
    public static PeriodIndicators Compute(EngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var currency = input.Currency;
        var period = input.Period;
        var carryOver = input.CarryOver ?? Money.Zero(currency);

        // RN-F02: received income with reception date inside the period.
        var receivedIncome = Money.Zero(currency);
        foreach (var income in input.Incomes)
        {
            if (income.IsReceived && income.ReceivedDate is { } d && period.Contains(d))
            {
                receivedIncome += income.Value;
            }
        }

        // RN-F03: expense payments in the period.
        var expensePaid = Money.Zero(currency);
        foreach (var expense in input.Expenses)
        {
            foreach (var payment in expense.Payments)
            {
                if (period.Contains(payment.Date))
                {
                    expensePaid += payment.Value;
                }
            }
        }

        // RN-F05: credit installment payments in the period (subset of paid expense).
        var creditPayments = Money.Zero(currency);
        foreach (var payment in input.CreditPayments)
        {
            if (period.Contains(payment.Date))
            {
                creditPayments += payment.Value;
            }
        }

        // RN-F03 total: expense payments plus credit payments (D-05). The credit
        // payment is counted here once, never as a separate movement.
        var paidExpense = expensePaid + creditPayments;

        // RN-F04: outstanding obligations of the period's expenses.
        var outstanding = Money.Zero(currency);
        foreach (var expense in input.Expenses)
        {
            if (period.Contains(expense.Date))
            {
                outstanding += expense.Outstanding();
            }
        }

        // RN-F06: savings movements in the period.
        var contributions = Money.Zero(currency);
        var withdrawals = Money.Zero(currency);
        foreach (var txn in input.Savings)
        {
            if (!period.Contains(txn.Date))
            {
                continue;
            }

            if (txn.IsContribution)
            {
                contributions += txn.Value;
            }
            else
            {
                withdrawals += txn.Value;
            }
        }

        // RN-F08: reserved = obligations + open credit installments due in the period.
        var pendingInstallments = Money.Zero(currency);
        foreach (var installment in input.CreditInstallments)
        {
            if (installment.IsOpen && period.Contains(installment.DueDate))
            {
                pendingInstallments += installment.Amount;
            }
        }

        var reserved = outstanding + pendingInstallments;

        // RN-F07: cash = carry-over + received income - paid expense - contributions + withdrawals.
        var cashOnHand = carryOver + receivedIncome - paidExpense - contributions + withdrawals;

        // RN-F09: free money = cash - reserved (may be negative).
        var freeMoney = cashOnHand - reserved;

        // RN-F14: budget consumption = paid expense / (received income + carry-over), 0 if denominator 0.
        var denominator = (receivedIncome + carryOver).Amount;
        var budgetConsumption = denominator == 0m
            ? 0m
            : paidExpense.Amount / denominator;

        return new PeriodIndicators
        {
            ReceivedIncome = receivedIncome.Round(),
            PaidExpense = paidExpense.Round(),
            CreditPayments = creditPayments.Round(),
            OutstandingObligations = outstanding.Round(),
            CarryOver = carryOver.Round(),
            SavingsContributions = contributions.Round(),
            SavingsWithdrawals = withdrawals.Round(),
            CashOnHand = cashOnHand.Round(),
            Reserved = reserved.Round(),
            FreeMoney = freeMoney.Round(),
            BudgetConsumption = budgetConsumption,
        };
    }
}
