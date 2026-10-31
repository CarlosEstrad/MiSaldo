using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Accounts;

/// <summary>
/// The personal data of a user (RF-PER-01). Only the fields listed in the SRS
/// are stored; the birth date is kept on <see cref="User"/> and is not editable
/// here (RF-PER-02, RF-PER-05).
/// </summary>
public sealed class UserProfile
{
    public UserProfile(
        Guid id,
        Guid userId,
        string fullName,
        string email,
        string country,
        string? gender = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name is required.", nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        Id = id;
        UserId = userId;
        FullName = fullName;
        Email = email;
        Country = country;
        Gender = gender;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public string FullName { get; private set; }

    public string Email { get; private set; }

    public string Country { get; private set; }

    public string? Gender { get; private set; }

    /// <summary>Updates editable profile fields. Email changes require external verification (RF-PER-03), handled above this layer.</summary>
    public void Update(string fullName, string country, string? gender)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name is required.", nameof(fullName));
        }

        FullName = fullName;
        Country = country;
        Gender = gender;
    }
}
