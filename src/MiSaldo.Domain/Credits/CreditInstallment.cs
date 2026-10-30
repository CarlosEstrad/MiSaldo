using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Credits;

/// <summary>
/// A single installment of a credit's schedule. Its amount can be recalculated
/// while it is open; once paid or cancelled it is frozen (RF-CRD-06). The
/// amount is managed by the owning <see cref="Credit"/> aggregate.
/// </summary>
public sealed class CreditInstallment
{
    internal CreditInstallment(int number, Money amount, DateOnly dueDate)
    {
        Number = number;
        Amount = amount;
        DueDate = dueDate;
        Status = InstallmentStatus.Open;
    }

    /// <summary>1-based position of the installment in the schedule.</summary>
    public int Number { get; }

    public Money Amount { get; private set; }

    public DateOnly DueDate { get; }

    public InstallmentStatus Status { get; private set; }

    public bool IsOpen => Status == InstallmentStatus.Open;

    internal void SetAmount(Money amount) => Amount = amount;

    internal void MarkPaid() => Status = InstallmentStatus.Paid;

    internal void Cancel() => Status = InstallmentStatus.Cancelled;
}
