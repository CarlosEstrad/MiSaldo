using MiSaldo.Domain.Accounts;

namespace MiSaldo.Application.Abstractions;

/// <summary>
/// Access to the authentication data: credentials, refresh tokens and one-time
/// tokens. Tokens are always looked up by their hash; the raw value is never
/// stored (RNF-SEG-02).
/// </summary>
public interface IAuthRepository
{
    Task<UserCredential?> GetCredentialAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>The user's refresh tokens that are still usable, to revoke them all (password reset).</summary>
    Task<IReadOnlyList<RefreshToken>> ListActiveRefreshTokensAsync(
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<OneTimeToken?> GetOneTimeTokenAsync(
        string tokenHash, OneTimeTokenPurpose purpose, CancellationToken cancellationToken = default);

    void Add(UserCredential credential);

    void Add(RefreshToken refreshToken);

    void Add(OneTimeToken oneTimeToken);
}
