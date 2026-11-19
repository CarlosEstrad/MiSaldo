namespace MiSaldo.Domain.Accounts;

/// <summary>
/// A long-lived refresh token, stored only by its hash so it can be revoked
/// (RNF-SEG-02). Each use rotates it: the old token is consumed and replaced by
/// a new one. Presenting a consumed token again is rejected (RF-AUT-05).
/// </summary>
public sealed class RefreshToken
{
    public RefreshToken(
        Guid id,
        Guid userId,
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
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public string TokenHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    /// <summary>When the token was rotated or revoked, or null while it is still usable.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    /// <summary>Hash of the token that replaced this one when it was rotated.</summary>
    public string? ReplacedByTokenHash { get; private set; }

    /// <summary>True while the token has not been consumed and has not expired.</summary>
    public bool IsActive(DateTimeOffset now) => ConsumedAt is null && now < ExpiresAt;

    /// <summary>Consumes this token and records the one that replaces it.</summary>
    public void Rotate(DateTimeOffset now, string newTokenHash)
    {
        if (string.IsNullOrWhiteSpace(newTokenHash))
        {
            throw new ArgumentException("The replacement token hash is required.", nameof(newTokenHash));
        }

        if (!IsActive(now))
        {
            throw new InvalidOperationException("Only an active refresh token can be rotated.");
        }

        ConsumedAt = now;
        ReplacedByTokenHash = newTokenHash;
    }

    /// <summary>Invalidates the token without replacing it (logout). Revoking twice changes nothing.</summary>
    public void Revoke(DateTimeOffset now) => ConsumedAt ??= now;
}
