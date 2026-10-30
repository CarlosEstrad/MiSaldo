using MiSaldo.Domain.Common;
using MiSaldo.Domain.Credits;
using MiSaldo.Domain.Expenses;
using MiSaldo.Domain.Incomes;
using MiSaldo.Domain.Periods;
using MiSaldo.Domain.Savings;

namespace MiSaldo.Domain.Engine;

/// <summary>
/// Everything the engine needs to compute one period's indicators. The caller
/// supplies the movements and the carry-over; the engine filters each movement
/// by its own date against the period range (RN-M02) and never mutates anything.
/// </summary>
public sealed record EngineInput
{
    public required FinancialPeriod Period { get; init; }

    public required Currency Currency { get; init; }

    /// <summary>Opening balance carried from the previous period (RN-P05, D-03).</summary>
    public Money? CarryOver { get; init; }

    public IReadOnlyList<IncomeTransaction> Incomes { get; init; } = [];

    public IReadOnlyList<Expense> Expenses { get; init; } = [];

    public IReadOnlyList<SavingsTransaction> Savings { get; init; } = [];

    /// <summary>Open credit installments due within the period count as reserved (RN-F08).</summary>
    public IReadOnlyList<CreditInstallment> CreditInstallments { get; init; } = [];

    /// <summary>Credit installment payments; those dated in the period count as paid expense (RN-F03).</summary>
    public IReadOnlyList<CreditPayment> CreditPayments { get; init; } = [];
}
