namespace MiSaldo.Domain.Accounts;

/// <summary>What a one-time token allows its holder to do.</summary>
public enum OneTimeTokenPurpose
{
    EmailVerification,
    PasswordReset,
}

/// <summary>
/// A single-use token with an expiry, stored only by its hash. Used for email
/// verification and password reset. Once consumed or expired it can never be
/// used again (RNF-SEG-02).
/// </summary>
public sealed class OneTimeToken
{
    public OneTimeToken(
        Guid id,
        Guid userId,
        OneTimeTokenPurpose purpose,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("A token hash is required.", nameof(tokenHash));
        }

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException("The token must expire after it is created.", nameof(expiresAt));
        }

        Id = id;
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public OneTimeTokenPurpose Purpose { get; }

    public string TokenHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>True while the token has not been used and has not expired.</summary>
    public bool IsUsable(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;

    /// <summary>Marks the token as used. A token that is not usable cannot be consumed.</summary>
    public void Consume(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new InvalidOperationException("The token was already used or has expired.");
        }

        ConsumedAt = now;
    }
}
