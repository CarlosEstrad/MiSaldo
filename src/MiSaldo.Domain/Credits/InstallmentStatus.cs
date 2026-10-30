namespace MiSaldo.Domain.Credits;

/// <summary>
/// The state of a single credit installment. Open installments can be
/// recalculated after a payment; paid and cancelled installments cannot
/// (RF-CRD-06, RF-CRD-09).
/// </summary>
public enum InstallmentStatus
{
    Open,
    Paid,
    Cancelled,
}
