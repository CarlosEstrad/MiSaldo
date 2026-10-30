namespace MiSaldo.Domain.Credits;

/// <summary>
/// How often credit installments fall due. Only monthly is supported in F2;
/// biweekly and weekly come in a later phase (D-11).
/// </summary>
public enum PaymentFrequency
{
    Monthly,
    Biweekly,
    Weekly,
}
