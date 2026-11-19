using MiSaldo.Domain.Accounts;
using MiSaldo.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Verifies the authentication tables against real PostgreSQL: credentials,
/// refresh tokens and one-time tokens, including the unique token hash and the
/// concurrency guard that stops two requests from using the same token.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class AuthRepositoryIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public AuthRepositoryIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> SeedUserAsync()
    {
        var userId = Guid.NewGuid();
        await using var db = _fixture.CreateContext(null);
        db.Users.Add(User.Register(userId, $"ext-{userId}", new DateOnly(1990, 1, 1), new DateOnly(2026, 1, 15)));
        await db.SaveChangesAsync(Ct);
        return userId;
    }

    private static string NewHash() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Credential_round_trips_with_its_lockout_state()
    {
        var userId = await SeedUserAsync();
        var now = DateTimeOffset.UtcNow;

        await using (var db = _fixture.CreateContext(null))
        {
            var credential = new UserCredential(Guid.NewGuid(), userId, "stored-hash");
            credential.MarkEmailVerified();
            credential.RegisterFailedLogin(now, 1, TimeSpan.FromMinutes(15)); // locks immediately
            new AuthRepository(db).Add(credential);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);

            Assert.NotNull(credential);
            Assert.Equal("stored-hash", credential.PasswordHash);
            Assert.True(credential.EmailVerified);
            Assert.True(credential.IsLocked(now));
            Assert.False(credential.IsLocked(now.AddMinutes(16)));

            var createdAt = (DateTimeOffset)db.Entry(credential).Property("CreatedAt").CurrentValue!;
            Assert.True(createdAt > now.AddMinutes(-5), "CreatedAt must be stamped on insert.");
        }
    }

    [Fact]
    public async Task Credential_changes_are_persisted()
    {
        var userId = await SeedUserAsync();

        await using (var db = _fixture.CreateContext(null))
        {
            new AuthRepository(db).Add(new UserCredential(Guid.NewGuid(), userId, "old-hash"));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);
            credential!.ChangePasswordHash("new-hash");
            credential.MarkEmailVerified();
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);
            Assert.Equal("new-hash", credential!.PasswordHash);
            Assert.True(credential.EmailVerified);
        }
    }

    [Fact]
    public async Task Refresh_token_is_found_by_hash_and_active_ones_are_listed()
    {
        var userId = await SeedUserAsync();
        var now = DateTimeOffset.UtcNow;
        var activeHash = NewHash();
        var consumedHash = NewHash();
        var expiredHash = NewHash();

        await using (var db = _fixture.CreateContext(null))
        {
            var repo = new AuthRepository(db);
            repo.Add(new RefreshToken(Guid.NewGuid(), userId, activeHash, now, now.AddDays(14)));

            var consumed = new RefreshToken(Guid.NewGuid(), userId, consumedHash, now, now.AddDays(14));
            consumed.Rotate(now, NewHash());
            repo.Add(consumed);

            repo.Add(new RefreshToken(Guid.NewGuid(), userId, expiredHash, now.AddHours(-2), now.AddHours(-1)));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var repo = new AuthRepository(db);

            var found = await repo.GetRefreshTokenAsync(activeHash, Ct);
            Assert.NotNull(found);
            Assert.Equal(userId, found.UserId);
            Assert.Null(await repo.GetRefreshTokenAsync("unknown-hash", Ct));

            var active = await repo.ListActiveRefreshTokensAsync(userId, now, Ct);
            Assert.Equal(activeHash, Assert.Single(active).TokenHash);

            var consumedLoaded = await repo.GetRefreshTokenAsync(consumedHash, Ct);
            Assert.NotNull(consumedLoaded!.ConsumedAt);
            Assert.NotNull(consumedLoaded.ReplacedByTokenHash);
        }
    }

    [Fact]
    public async Task One_time_token_is_found_only_for_its_own_purpose()
    {
        var userId = await SeedUserAsync();
        var now = DateTimeOffset.UtcNow;
        var hash = NewHash();

        await using (var db = _fixture.CreateContext(null))
        {
            new AuthRepository(db).Add(new OneTimeToken(
                Guid.NewGuid(), userId, OneTimeTokenPurpose.EmailVerification, hash, now, now.AddHours(24)));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var repo = new AuthRepository(db);

            Assert.NotNull(await repo.GetOneTimeTokenAsync(hash, OneTimeTokenPurpose.EmailVerification, Ct));
            Assert.Null(await repo.GetOneTimeTokenAsync(hash, OneTimeTokenPurpose.PasswordReset, Ct));
        }
    }

    [Fact]
    public async Task Consuming_a_one_time_token_is_persisted()
    {
        var userId = await SeedUserAsync();
        var now = DateTimeOffset.UtcNow;
        var hash = NewHash();

        await using (var db = _fixture.CreateContext(null))
        {
            new AuthRepository(db).Add(new OneTimeToken(
                Guid.NewGuid(), userId, OneTimeTokenPurpose.PasswordReset, hash, now, now.AddHours(1)));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var token = await new AuthRepository(db).GetOneTimeTokenAsync(hash, OneTimeTokenPurpose.PasswordReset, Ct);
            token!.Consume(now.AddMinutes(1));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var token = await new AuthRepository(db).GetOneTimeTokenAsync(hash, OneTimeTokenPurpose.PasswordReset, Ct);
            Assert.False(token!.IsUsable(now.AddMinutes(2)));
        }
    }

    [Fact]
    public async Task A_duplicate_refresh_token_hash_is_rejected()
    {
        var userId = await SeedUserAsync();
        var now = DateTimeOffset.UtcNow;
        var hash = NewHash();

        await using (var db = _fixture.CreateContext(null))
        {
            new AuthRepository(db).Add(new RefreshToken(Guid.NewGuid(), userId, hash, now, now.AddDays(1)));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            new AuthRepository(db).Add(new RefreshToken(Guid.NewGuid(), userId, hash, now, now.AddDays(1)));
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }
    }

    // RNF-SEG-02: if the same refresh token is presented twice at once, only one
    // rotation may win; the other must fail instead of issuing a second session.
    [Fact]
    public async Task Two_concurrent_rotations_of_the_same_refresh_token_cannot_both_succeed()
    {
        var userId = await SeedUserAsync();
        var now = DateTimeOffset.UtcNow;
        var hash = NewHash();

        await using (var db = _fixture.CreateContext(null))
        {
            new AuthRepository(db).Add(new RefreshToken(Guid.NewGuid(), userId, hash, now, now.AddDays(14)));
            await db.SaveChangesAsync(Ct);
        }

        await using var first = _fixture.CreateContext(null);
        await using var second = _fixture.CreateContext(null);
        var tokenInFirst = await new AuthRepository(first).GetRefreshTokenAsync(hash, Ct);
        var tokenInSecond = await new AuthRepository(second).GetRefreshTokenAsync(hash, Ct);

        tokenInFirst!.Rotate(now, NewHash());
        await first.SaveChangesAsync(Ct);

        tokenInSecond!.Rotate(now, NewHash()); // it still looked active in this context
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
    }
}
