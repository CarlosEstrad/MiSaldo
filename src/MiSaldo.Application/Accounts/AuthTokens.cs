using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;

namespace MiSaldo.Application.Accounts;

/// <summary>
/// The tokens returned to the client after a login or a refresh. The refresh
/// token value is shown only here; the database keeps its hash.
/// </summary>
public sealed record AuthTokens(
    Guid UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool EmailVerified)
{
    public override string ToString() => Redaction.Of(nameof(AuthTokens));
}

/// <summary>Issues a new session: a signed access token plus a stored refresh token.</summary>
internal static class SessionTokens
{
    public static AuthTokens Issue(
        IAuthRepository auth,
        ITokenService tokens,
        Guid userId,
        bool emailVerified,
        DateTimeOffset now,
        OpaqueToken refreshToken)
    {
        var refreshExpiresAt = now + tokens.RefreshTokenLifetime;
        auth.Add(new RefreshToken(Guid.NewGuid(), userId, refreshToken.Hash, now, refreshExpiresAt));

        var access = tokens.CreateAccessToken(userId, emailVerified);
        return new AuthTokens(
            userId, access.Value, access.ExpiresAt, refreshToken.Value, refreshExpiresAt, emailVerified);
    }
}
