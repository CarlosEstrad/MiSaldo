using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Engine;

/// <summary>
/// The computed indicators of a period. Every value comes from the movements
/// through <see cref="FinancialEngine"/>; nothing here is stored or recomputed
/// by any client (RN-F02 to RN-F14).
/// </summary>
public sealed record PeriodIndicators
{
    /// <summary>Received income with reception date inside the period (RN-F02).</summary>
    public required Money ReceivedIncome { get; init; }

    /// <summary>
    /// Expenses paid in the period, including credit installment payments
    /// (RN-F03, D-05). The credit payment is counted here, not as a separate line.
    /// </summary>
    public required Money PaidExpense { get; init; }

    /// <summary>
    /// Credit installment payments in the period (RN-F05). A subset of
    /// <see cref="PaidExpense"/>, exposed only for traceability, never added again.
    /// </summary>
    public required Money CreditPayments { get; init; }

    /// <summary>Outstanding obligations of the period's expenses (RN-F04).</summary>
    public required Money OutstandingObligations { get; init; }

    /// <summary>Carry-over applied as the opening balance (RN-P05, D-03).</summary>
    public required Money CarryOver { get; init; }

    /// <summary>Savings contributions in the period (RN-F06).</summary>
    public required Money SavingsContributions { get; init; }

    /// <summary>Savings withdrawals in the period (RN-F06).</summary>
    public required Money SavingsWithdrawals { get; init; }

    /// <summary>Cash on hand (RN-F07).</summary>
    public required Money CashOnHand { get; init; }

    /// <summary>Reserved money: obligations plus pending credit installments (RN-F08).</summary>
    public required Money Reserved { get; init; }

    /// <summary>Free money: cash minus reserved; can be negative (RN-F09).</summary>
    public required Money FreeMoney { get; init; }

    /// <summary>Budget consumption ratio, 0 when the denominator is zero (RN-F14).</summary>
    public required decimal BudgetConsumption { get; init; }
}
