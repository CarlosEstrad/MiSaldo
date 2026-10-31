namespace MiSaldo.Domain.Common;

/// <summary>
/// Thrown when an account is created for someone under the minimum age. At
/// launch MiSaldo is only for people 18 or older; the registration is rejected
/// and no account or data is stored (RF-AUT-02, decisión 7).
/// </summary>
public sealed class UnderageUserException : DomainException
{
    public UnderageUserException(int ageYears, int minimumAge)
        : base($"User must be at least {minimumAge} years old, but is {ageYears}.")
    {
        AgeYears = ageYears;
        MinimumAge = minimumAge;
    }

    public int AgeYears { get; }

    public int MinimumAge { get; }
}
