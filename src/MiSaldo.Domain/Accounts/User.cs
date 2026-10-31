using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Accounts;

/// <summary>
/// A MiSaldo account, linked to an external identity (Cognito). At launch, only
/// users 18 or older are allowed; the birth date is required and never editable
/// by the user (RF-AUT-02, RF-PER-02, decisión 7). The model carries
/// <see cref="AccountType"/> and a nullable <see cref="GuardianUserId"/> to
/// anticipate future family accounts without using them now.
/// </summary>
public sealed class User
{
    public const int MinimumAgeYears = 18;

    private User(
        Guid id,
        string externalId,
        DateOnly birthDate,
        AccountType accountType,
        Guid? guardianUserId)
    {
        Id = id;
        ExternalId = externalId;
        BirthDate = birthDate;
        AccountType = accountType;
        GuardianUserId = guardianUserId;
    }

    public Guid Id { get; }

    /// <summary>Identifier from the external identity provider (Cognito).</summary>
    public string ExternalId { get; }

    public DateOnly BirthDate { get; }

    public AccountType AccountType { get; }

    public Guid? GuardianUserId { get; }

    /// <summary>
    /// Registers a standard account, enforcing the minimum age against the given
    /// reference date. Throws <see cref="UnderageUserException"/> when the user
    /// is under 18 (RF-AUT-02).
    /// </summary>
    public static User Register(Guid id, string externalId, DateOnly birthDate, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("External id is required.", nameof(externalId));
        }

        var age = AgeOn(birthDate, today);
        if (age < MinimumAgeYears)
        {
            throw new UnderageUserException(age, MinimumAgeYears);
        }

        return new User(id, externalId, birthDate, AccountType.Standard, guardianUserId: null);
    }

    /// <summary>Whole years from birth date to the reference date.</summary>
    public static int AgeOn(DateOnly birthDate, DateOnly today)
    {
        var age = today.Year - birthDate.Year;
        if (today < birthDate.AddYears(age))
        {
            age--;
        }

        return age;
    }
}
