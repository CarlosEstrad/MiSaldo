namespace MiSaldo.Domain.Accounts;

/// <summary>
/// Records a user's acceptance of the privacy policy and the terms of use,
/// keeping the accepted version and the acceptance timestamp (RF-PRV-01).
/// </summary>
public sealed class ConsentRecord
{
    public ConsentRecord(
        Guid id,
        Guid userId,
        string privacyPolicyVersion,
        string termsVersion,
        DateTimeOffset acceptedAt)
    {
        if (string.IsNullOrWhiteSpace(privacyPolicyVersion))
        {
            throw new ArgumentException("Privacy policy version is required.", nameof(privacyPolicyVersion));
        }

        if (string.IsNullOrWhiteSpace(termsVersion))
        {
            throw new ArgumentException("Terms version is required.", nameof(termsVersion));
        }

        Id = id;
        UserId = userId;
        PrivacyPolicyVersion = privacyPolicyVersion;
        TermsVersion = termsVersion;
        AcceptedAt = acceptedAt;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public string PrivacyPolicyVersion { get; }

    public string TermsVersion { get; }

    public DateTimeOffset AcceptedAt { get; }
}
