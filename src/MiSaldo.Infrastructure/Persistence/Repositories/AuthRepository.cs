using MiSaldo.Application.Abstractions;
using MiSaldo.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence.Repositories;

public sealed class AuthRepository : IAuthRepository
{
    private readonly MiSaldoDbContext _db;

    public AuthRepository(MiSaldoDbContext db) => _db = db;

    public Task<UserCredential?> GetCredentialAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.UserCredentials.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> ListActiveRefreshTokensAsync(
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.ConsumedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

    public Task<OneTimeToken?> GetOneTimeTokenAsync(
        string tokenHash, OneTimeTokenPurpose purpose, CancellationToken cancellationToken = default) =>
        _db.OneTimeTokens.FirstOrDefaultAsync(
            t => t.TokenHash == tokenHash && t.Purpose == purpose, cancellationToken);

    public async Task<IReadOnlyList<OneTimeToken>> ListUsableOneTimeTokensAsync(
        Guid userId, OneTimeTokenPurpose purpose, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await _db.OneTimeTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.ConsumedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(UserCredential credential) => _db.UserCredentials.Add(credential);

    public void Add(RefreshToken refreshToken) => _db.RefreshTokens.Add(refreshToken);

    public void Add(OneTimeToken oneTimeToken) => _db.OneTimeTokens.Add(oneTimeToken);
}
