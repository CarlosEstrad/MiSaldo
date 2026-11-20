using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Accounts;
using MiSaldo.Domain.Accounts;
using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Persistence;
using MiSaldo.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Spec 04, task 5 (🐳): resending the email verification against real
/// PostgreSQL through the app role. Needs Docker running.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ResendVerificationIntegrationTests
{
    private const string Password = "a long enough password";

    private static readonly PasswordHasher Hasher =
        new(new Argon2Options { MemorySizeKiB = 1024, Iterations = 1, Parallelism = 1 });

    private readonly PostgresFixture _fixture;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero));
    private readonly RecordingAccountNotifier _notifier = new();
    private readonly RegistrationOptions _options = new();

    public ResendVerificationIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private JwtTokenService Tokens => new(new JwtOptions { SigningKey = new string('k', 48) }, _clock);

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private List<AccountTokenMessage> SentTo(Guid userId) =>
        _notifier.EmailVerifications.Where(m => m.UserId == userId).ToList();

    private async Task<Guid> RegisterAsync(string email, string? language = null)
    {
        await using var db = _fixture.CreateContext(null);
        var currentUser = new TestCurrentUser();
        var result = await new RegisterUserHandler(
                new UserRepository(db), new AuthRepository(db), new NewUserDataSeeder(db, currentUser),
                new UnitOfWork(db), Hasher, Tokens, _clock, _notifier, _options, currentUser)
            .HandleAsync(new RegisterUserCommand(
                email, Password, "Ana Pérez", new DateOnly(1990, 5, 1), "CO", null, true, true, language), Ct);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value.UserId;
    }

    private async Task ResendAsync(string email)
    {
        await using var db = _fixture.CreateContext(null);
        var result = await new ResendVerificationHandler(
                new UserRepository(db), new AuthRepository(db), new UnitOfWork(db), Tokens, _clock, _notifier, _options)
            .HandleAsync(new ResendVerificationCommand(email), Ct);
        Assert.True(result.IsSuccess);
    }

    private async Task<bool> VerifyAsync(string token)
    {
        await using var db = _fixture.CreateContext(null);
        return (await new VerifyEmailHandler(new AuthRepository(db), new UnitOfWork(db), Tokens, _clock)
            .HandleAsync(new VerifyEmailCommand(token), Ct)).IsSuccess;
    }

    private async Task<bool> IsVerifiedAsync(Guid userId)
    {
        await using var db = _fixture.CreateContext(null);
        return (await new AuthRepository(db).GetCredentialAsync(userId, Ct))!.EmailVerified;
    }

    [Fact]
    public async Task A_resend_replaces_the_previous_link_and_the_new_one_verifies()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);
        var first = Assert.Single(SentTo(userId)).Token;

        _clock.UtcNow = _clock.UtcNow.AddSeconds(61);
        await ResendAsync($"  {email.ToUpperInvariant()} ");

        var sent = SentTo(userId);
        Assert.Equal(2, sent.Count);
        var second = sent[1];
        Assert.NotEqual(first, second.Token);
        Assert.Equal(_clock.UtcNow.AddHours(24), second.ExpiresAt);

        Assert.False(await VerifyAsync(first));
        Assert.False(await IsVerifiedAsync(userId));

        Assert.True(await VerifyAsync(second.Token));
        Assert.True(await IsVerifiedAsync(userId));
    }

    [Fact]
    public async Task Only_one_verification_token_stays_usable_after_a_resend()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(2);
        await ResendAsync(email);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(2);
        await ResendAsync(email);

        await using var db = _fixture.CreateContext(null);
        var usable = await new AuthRepository(db).ListUsableOneTimeTokensAsync(
            userId, OneTimeTokenPurpose.EmailVerification, _clock.UtcNow, Ct);
        var only = Assert.Single(usable);
        Assert.Equal(Tokens.HashToken(SentTo(userId)[^1].Token), only.TokenHash);
        Assert.Equal(3, await db.OneTimeTokens.CountAsync(t => t.UserId == userId, Ct));
    }

    [Fact]
    public async Task A_resend_within_a_minute_of_the_last_email_sends_nothing()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);
        var first = Assert.Single(SentTo(userId)).Token;

        _clock.UtcNow = _clock.UtcNow.AddSeconds(59);
        await ResendAsync(email);

        Assert.Single(SentTo(userId));
        Assert.True(await VerifyAsync(first));
    }

    [Fact]
    public async Task The_minute_counts_from_the_last_resend_too()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
        await ResendAsync(email);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(30);
        await ResendAsync(email);

        Assert.Equal(2, SentTo(userId).Count);
    }

    [Fact]
    public async Task An_expired_link_can_be_replaced()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);
        var first = Assert.Single(SentTo(userId)).Token;

        _clock.UtcNow = _clock.UtcNow.AddHours(25);
        Assert.False(await VerifyAsync(first));

        await ResendAsync(email);

        Assert.True(await VerifyAsync(SentTo(userId)[^1].Token));
        Assert.True(await IsVerifiedAsync(userId));
    }

    [Fact]
    public async Task The_new_email_uses_the_language_of_the_account()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email, "en");

        _clock.UtcNow = _clock.UtcNow.AddMinutes(2);
        await ResendAsync(email);

        Assert.All(SentTo(userId), m => Assert.Equal("en", m.Language));
    }

    [Fact]
    public async Task A_verified_account_gets_no_new_email()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);
        Assert.True(await VerifyAsync(Assert.Single(SentTo(userId)).Token));

        _clock.UtcNow = _clock.UtcNow.AddMinutes(2);
        await ResendAsync(email);

        Assert.Single(SentTo(userId));
        await using var db = _fixture.CreateContext(null);
        Assert.Equal(1, await db.OneTimeTokens.CountAsync(t => t.UserId == userId, Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nobody-here@example.com")]
    public async Task An_unknown_or_blank_email_succeeds_without_sending(string email)
    {
        var before = _notifier.EmailVerifications.Count;

        await ResendAsync(email);

        Assert.Equal(before, _notifier.EmailVerifications.Count);
    }

    [Fact]
    public async Task Listing_usable_tokens_skips_consumed_expired_and_other_purposes()
    {
        var userId = await RegisterAsync(UniqueEmail());
        var otherUserId = await RegisterAsync(UniqueEmail());
        var now = _clock.UtcNow;

        await using (var db = _fixture.CreateContext(null))
        {
            var auth = new AuthRepository(db);
            var consumed = new OneTimeToken(
                Guid.NewGuid(), userId, OneTimeTokenPurpose.EmailVerification, "consumed-" + Guid.NewGuid(), now, now.AddHours(1));
            consumed.Consume(now);
            auth.Add(consumed);
            auth.Add(new OneTimeToken(
                Guid.NewGuid(), userId, OneTimeTokenPurpose.EmailVerification, "expired-" + Guid.NewGuid(),
                now.AddHours(-2), now.AddHours(-1)));
            auth.Add(new OneTimeToken(
                Guid.NewGuid(), userId, OneTimeTokenPurpose.PasswordReset, "reset-" + Guid.NewGuid(), now, now.AddHours(1)));
            auth.Add(new OneTimeToken(
                Guid.NewGuid(), otherUserId, OneTimeTokenPurpose.EmailVerification, "other-user-" + Guid.NewGuid(), now, now.AddHours(1)));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var usable = await new AuthRepository(db).ListUsableOneTimeTokensAsync(
                userId, OneTimeTokenPurpose.EmailVerification, now, Ct);

            // Only the token issued at registration.
            var only = Assert.Single(usable);
            Assert.Equal(Tokens.HashToken(Assert.Single(SentTo(userId)).Token), only.TokenHash);
        }
    }
}
