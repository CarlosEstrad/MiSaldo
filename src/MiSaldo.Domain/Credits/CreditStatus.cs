namespace MiSaldo.Domain.Credits;

/// <summary>
/// The lifecycle state of a credit. A credit is active until its outstanding
/// balance reaches zero, at which point it is paid (RF-CRD-09).
/// </summary>
public enum CreditStatus
{
    Active,
    Paid,
}
