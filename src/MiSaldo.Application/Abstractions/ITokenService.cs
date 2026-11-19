namespace MiSaldo.Application.Abstractions;

/// <summary>A signed access token and the moment it stops being valid.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// A random opaque token (refresh, email verification, password reset). Only the
/// <see cref="Hash"/> is stored; the <see cref="Value"/> goes to the client once.
/// </summary>
public sealed record OpaqueToken(string Value, string Hash);

/// <summary>
/// Issues and hashes tokens. Access tokens are signed and stateless; opaque
/// tokens are stored by hash so they can be revoked (RNF-SEG-02). Hiding this
/// behind an interface lets the identity provider change (e.g. to Cognito)
/// without touching the use cases.
/// </summary>
public interface ITokenService
{
    /// <summary>How long a refresh token stays valid before it must be renewed by logging in again.</summary>
    TimeSpan RefreshTokenLifetime { get; }

    /// <summary>Creates a short-lived signed access token carrying the user id.</summary>
    AccessToken CreateAccessToken(Guid userId, bool emailVerified);

    /// <summary>Creates a random opaque token and its hash.</summary>
    OpaqueToken CreateOpaqueToken();

    /// <summary>Hashes a token presented by a client so it can be looked up.</summary>
    string HashToken(string token);
}
