using MiSaldo.Application.Accounts;
using MiSaldo.Application.Common;
using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Persistence;
using MiSaldo.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Runs login, refresh, logout and password reset against real PostgreSQL
/// through the app role, as the API will (task 5).
/// </summary>
[Collection(nameof(PostgresCollection))]
public class SessionIntegrationTests
{
    private const string Password = "a long enough password";

    private static readonly PasswordHasher Hasher =
        new(new Argon2Options { MemorySizeKiB = 1024, Iterations = 1, Parallelism = 1 });

    private readonly PostgresFixture _fixture;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero));
    private readonly RecordingAccountNotifier _notifier = new();
    private readonly AuthenticationOptions _options = new();

    public SessionIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private JwtTokenService Tokens => new(new JwtOptions { SigningKey = new string('k', 48) }, _clock);

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    // --- helpers ---

    private async Task<Guid> RegisterAsync(string email, bool verify = true)
    {
        Guid userId;
        await using (var db = _fixture.CreateContext(null))
        {
            var currentUser = new TestCurrentUser();
            var handler = new RegisterUserHandler(
                new UserRepository(db), new AuthRepository(db), new NewUserDataSeeder(db, currentUser),
                new UnitOfWork(db), Hasher, Tokens, _clock, _notifier, new RegistrationOptions(), currentUser);

            var result = await handler.HandleAsync(new RegisterUserCommand(
                email, Password, "Ana Pérez", new DateOnly(1990, 5, 1), "CO", null, true, true), Ct);
            Assert.True(result.IsSuccess, result.Error?.Message);
            userId = result.Value.UserId;
        }

        if (verify)
        {
            var token = _notifier.EmailVerifications.Single(m => m.UserId == userId).Token;
            await using var db = _fixture.CreateContext(null);
            var verified = await new VerifyEmailHandler(new AuthRepository(db), new UnitOfWork(db), Tokens, _clock)
                .HandleAsync(new VerifyEmailCommand(token), Ct);
            Assert.True(verified.IsSuccess, verified.Error?.Message);
        }

        return userId;
    }

    private async Task<Result<AuthTokens>> LoginAsync(string email, string password = Password)
    {
        await using var db = _fixture.CreateContext(null);
        return await new LoginHandler(
                new UserRepository(db), new AuthRepository(db), new UnitOfWork(db), Hasher, Tokens, _clock, _options)
            .HandleAsync(new LoginCommand(email, password), Ct);
    }

    private async Task<AuthTokens> LoginOkAsync(string email, string password = Password)
    {
        var result = await LoginAsync(email, password);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private async Task<Result<AuthTokens>> RefreshAsync(string refreshToken)
    {
        await using var db = _fixture.CreateContext(null);
        return await new RefreshSessionHandler(new AuthRepository(db), new UnitOfWork(db), Tokens, _clock)
            .HandleAsync(new RefreshSessionCommand(refreshToken), Ct);
    }

    private async Task<Result> LogoutAsync(string refreshToken)
    {
        await using var db = _fixture.CreateContext(null);
        return await new LogoutHandler(new AuthRepository(db), new UnitOfWork(db), Tokens, _clock)
            .HandleAsync(new LogoutCommand(refreshToken), Ct);
    }

    private async Task<Result> ForgotAsync(string email)
    {
        await using var db = _fixture.CreateContext(null);
        return await new ForgotPasswordHandler(
                new UserRepository(db), new AuthRepository(db), new UnitOfWork(db), Tokens, _clock, _notifier, _options)
            .HandleAsync(new ForgotPasswordCommand(email), Ct);
    }

    private async Task<Result> ResetAsync(string token, string newPassword)
    {
        await using var db = _fixture.CreateContext(null);
        return await new ResetPasswordHandler(new AuthRepository(db), new UnitOfWork(db), Hasher, Tokens, _clock)
            .HandleAsync(new ResetPasswordCommand(token, newPassword), Ct);
    }

    private static void AssertInvalidCredentials(Result<AuthTokens> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(LoginHandler.InvalidCredentialsCode, result.Error!.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    private static void AssertInvalidRefresh(Result<AuthTokens> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(RefreshSessionHandler.InvalidRefreshTokenCode, result.Error!.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    // --- 5.1 Login ---

    [Fact]
    public async Task A_valid_login_issues_an_access_token_and_a_stored_refresh_token()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);

        var tokens = await LoginOkAsync($"  {email.ToUpperInvariant()} ");

        Assert.Equal(userId, tokens.UserId);
        Assert.True(tokens.EmailVerified);
        Assert.Equal(_clock.UtcNow.AddMinutes(30), tokens.AccessTokenExpiresAt);
        Assert.Equal(_clock.UtcNow.AddDays(14), tokens.RefreshTokenExpiresAt);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);
        Assert.Equal(userId.ToString(), jwt.GetClaim(JwtClaims.Subject).Value);
        Assert.Equal("true", jwt.GetClaim(JwtClaims.EmailVerified).Value, ignoreCase: true);

        await using var db = _fixture.CreateContext(null);
        var stored = await new AuthRepository(db).GetRefreshTokenAsync(Tokens.HashToken(tokens.RefreshToken), Ct);
        Assert.NotNull(stored);
        Assert.Equal(userId, stored.UserId);
        Assert.True(stored.IsActive(_clock.UtcNow));
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.TokenHash == tokens.RefreshToken, Ct));
    }

    [Fact]
    public async Task A_login_with_an_unverified_email_succeeds_but_the_token_says_so()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, verify: false);

        var tokens = await LoginOkAsync(email);

        Assert.False(tokens.EmailVerified);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);
        Assert.Equal("false", jwt.GetClaim(JwtClaims.EmailVerified).Value, ignoreCase: true);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_same_generic_error()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var wrongPassword = await LoginAsync(email, "not the right password");
        var unknownEmail = await LoginAsync(UniqueEmail());

        AssertInvalidCredentials(wrongPassword);
        AssertInvalidCredentials(unknownEmail);
        Assert.Equal(wrongPassword.Error!.Message, unknownEmail.Error!.Message);
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData("someone@example.com", "")]
    public async Task Blank_credentials_get_the_generic_error(string email, string password)
    {
        AssertInvalidCredentials(await LoginAsync(email, password));
    }

    [Fact]
    public async Task The_account_locks_after_five_failures_and_unlocks_after_fifteen_minutes()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);

        for (var i = 0; i < 5; i++)
        {
            AssertInvalidCredentials(await LoginAsync(email, "not the right password"));
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);
            Assert.Equal(_clock.UtcNow.AddMinutes(15), credential!.LockedUntil);
        }

        // Locked: even the right password is refused, with the same message.
        var whileLocked = await LoginAsync(email);
        AssertInvalidCredentials(whileLocked);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(14);
        AssertInvalidCredentials(await LoginAsync(email));

        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await LoginOkAsync(email);

        await using (var db = _fixture.CreateContext(null))
        {
            var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);
            Assert.Null(credential!.LockedUntil);
            Assert.Equal(0, credential.FailedLoginAttempts);
        }
    }

    [Fact]
    public async Task A_successful_login_resets_the_failure_counter()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);

        for (var i = 0; i < 4; i++)
        {
            AssertInvalidCredentials(await LoginAsync(email, "not the right password"));
        }

        await LoginOkAsync(email);
        AssertInvalidCredentials(await LoginAsync(email, "not the right password"));

        await using var db = _fixture.CreateContext(null);
        var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);
        Assert.Equal(1, credential!.FailedLoginAttempts);
        Assert.Null(credential.LockedUntil);
    }

    // --- 5.2 Refresh and logout ---

    [Fact]
    public async Task A_refresh_rotates_the_token_and_the_old_one_stops_working()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);
        var first = await LoginOkAsync(email);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(20);
        var refreshed = await RefreshAsync(first.RefreshToken);

        Assert.True(refreshed.IsSuccess, refreshed.Error?.Message);
        var second = refreshed.Value;
        Assert.Equal(userId, second.UserId);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.Equal(_clock.UtcNow.AddMinutes(30), second.AccessTokenExpiresAt);

        await using (var db = _fixture.CreateContext(null))
        {
            var old = await new AuthRepository(db).GetRefreshTokenAsync(Tokens.HashToken(first.RefreshToken), Ct);
            Assert.Equal(_clock.UtcNow, old!.ConsumedAt);
            Assert.Equal(Tokens.HashToken(second.RefreshToken), old.ReplacedByTokenHash);
        }

        AssertInvalidRefresh(await RefreshAsync(first.RefreshToken));
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_every_session_of_the_user()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var phone = await LoginOkAsync(email);
        var laptop = await LoginOkAsync(email);

        var rotated = await RefreshAsync(phone.RefreshToken);
        Assert.True(rotated.IsSuccess, rotated.Error?.Message);

        // Someone replays the old phone token.
        AssertInvalidRefresh(await RefreshAsync(phone.RefreshToken));

        AssertInvalidRefresh(await RefreshAsync(rotated.Value.RefreshToken));
        AssertInvalidRefresh(await RefreshAsync(laptop.RefreshToken));
    }

    [Fact]
    public async Task Reuse_detection_does_not_touch_other_users()
    {
        var victimEmail = UniqueEmail();
        var otherEmail = UniqueEmail();
        await RegisterAsync(victimEmail);
        await RegisterAsync(otherEmail);

        var victim = await LoginOkAsync(victimEmail);
        var other = await LoginOkAsync(otherEmail);

        Assert.True((await RefreshAsync(victim.RefreshToken)).IsSuccess);
        AssertInvalidRefresh(await RefreshAsync(victim.RefreshToken));

        Assert.True((await RefreshAsync(other.RefreshToken)).IsSuccess);
    }

    [Fact]
    public async Task An_expired_refresh_token_is_rejected()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var tokens = await LoginOkAsync(email);

        _clock.UtcNow = _clock.UtcNow.AddDays(14);

        AssertInvalidRefresh(await RefreshAsync(tokens.RefreshToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task An_unknown_or_blank_refresh_token_is_rejected(string token)
    {
        AssertInvalidRefresh(await RefreshAsync(token));
    }

    [Fact]
    public async Task A_refresh_reports_a_newly_verified_email()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email, verify: false);
        var tokens = await LoginOkAsync(email);
        Assert.False(tokens.EmailVerified);

        var verificationToken = _notifier.EmailVerifications.Single(m => m.UserId == userId).Token;
        await using (var db = _fixture.CreateContext(null))
        {
            Assert.True((await new VerifyEmailHandler(new AuthRepository(db), new UnitOfWork(db), Tokens, _clock)
                .HandleAsync(new VerifyEmailCommand(verificationToken), Ct)).IsSuccess);
        }

        var refreshed = await RefreshAsync(tokens.RefreshToken);
        Assert.True(refreshed.IsSuccess, refreshed.Error?.Message);
        Assert.True(refreshed.Value.EmailVerified);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_can_be_repeated()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        var tokens = await LoginOkAsync(email);
        var otherSession = await LoginOkAsync(email);

        Assert.True((await LogoutAsync(tokens.RefreshToken)).IsSuccess);
        AssertInvalidRefresh(await RefreshAsync(tokens.RefreshToken));

        Assert.True((await LogoutAsync(tokens.RefreshToken)).IsSuccess);
        Assert.True((await LogoutAsync("not-a-real-token")).IsSuccess);
        Assert.True((await LogoutAsync("")).IsSuccess);

        // Logging out one session leaves the others open.
        Assert.True((await RefreshAsync(otherSession.RefreshToken)).IsSuccess);
    }

    // --- 5.3 Password reset ---

    [Fact]
    public async Task Forgot_password_answers_the_same_whether_or_not_the_email_exists()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);

        var known = await ForgotAsync(email.ToUpperInvariant());
        var unknown = await ForgotAsync(UniqueEmail());
        var blank = await ForgotAsync("  ");

        Assert.True(known.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.True(blank.IsSuccess);

        var message = Assert.Single(_notifier.PasswordResets);
        Assert.Equal(userId, message.UserId);
        Assert.Equal(email, message.Email);

        await using var db = _fixture.CreateContext(null);
        var stored = await new AuthRepository(db).GetOneTimeTokenAsync(
            Tokens.HashToken(message.Token), Domain.Accounts.OneTimeTokenPurpose.PasswordReset, Ct);
        Assert.NotNull(stored);
        Assert.Equal(_clock.UtcNow.AddHours(1), stored.ExpiresAt);
    }

    [Fact]
    public async Task A_reset_changes_the_password_and_closes_every_session()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);
        var session = await LoginOkAsync(email);

        string oldHash;
        await using (var db = _fixture.CreateContext(null))
        {
            oldHash = (await new AuthRepository(db).GetCredentialAsync(userId, Ct))!.PasswordHash;
        }

        Assert.True((await ForgotAsync(email)).IsSuccess);
        var token = Assert.Single(_notifier.PasswordResets).Token;

        var reset = await ResetAsync(token, "a brand new password");
        Assert.True(reset.IsSuccess, reset.Error?.Message);

        await using (var db = _fixture.CreateContext(null))
        {
            var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);
            Assert.NotEqual(oldHash, credential!.PasswordHash);
            Assert.True(Hasher.Verify(credential.PasswordHash, "a brand new password"));
        }

        AssertInvalidRefresh(await RefreshAsync(session.RefreshToken));
        AssertInvalidCredentials(await LoginAsync(email));
        await LoginOkAsync(email, "a brand new password");
    }

    [Fact]
    public async Task A_reset_clears_an_active_lockout()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        for (var i = 0; i < 5; i++)
        {
            AssertInvalidCredentials(await LoginAsync(email, "not the right password"));
        }

        Assert.True((await ForgotAsync(email)).IsSuccess);
        var token = Assert.Single(_notifier.PasswordResets).Token;
        Assert.True((await ResetAsync(token, "a brand new password")).IsSuccess);

        await LoginOkAsync(email, "a brand new password");
    }

    [Fact]
    public async Task A_reset_token_works_only_once()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        Assert.True((await ForgotAsync(email)).IsSuccess);
        var token = Assert.Single(_notifier.PasswordResets).Token;

        Assert.True((await ResetAsync(token, "a brand new password")).IsSuccess);

        var second = await ResetAsync(token, "yet another password");
        Assert.True(second.IsFailure);
        Assert.Equal(ResetPasswordHandler.InvalidTokenCode, second.Error!.Code);
        await LoginOkAsync(email, "a brand new password");
    }

    [Fact]
    public async Task An_expired_reset_token_is_rejected()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        Assert.True((await ForgotAsync(email)).IsSuccess);
        var token = Assert.Single(_notifier.PasswordResets).Token;

        _clock.UtcNow = _clock.UtcNow.AddHours(1);

        var result = await ResetAsync(token, "a brand new password");
        Assert.True(result.IsFailure);
        Assert.Equal(ResetPasswordHandler.InvalidTokenCode, result.Error!.Code);
        await LoginOkAsync(email);
    }

    [Fact]
    public async Task A_short_new_password_is_rejected_and_the_token_stays_usable()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);
        Assert.True((await ForgotAsync(email)).IsSuccess);
        var token = Assert.Single(_notifier.PasswordResets).Token;

        var result = await ResetAsync(token, "short");
        Assert.True(result.IsFailure);
        Assert.Equal(PasswordPolicy.TooShortCode, result.Error!.Code);

        Assert.True((await ResetAsync(token, "a brand new password")).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task An_unknown_or_blank_reset_token_is_rejected(string token)
    {
        var result = await ResetAsync(token, "a brand new password");

        Assert.True(result.IsFailure);
        Assert.Equal(ResetPasswordHandler.InvalidTokenCode, result.Error!.Code);
    }
}
