using MiSaldo.Application.Accounts;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;
using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Persistence;
using MiSaldo.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Runs the registration and email verification use cases against real
/// PostgreSQL, through the app role with Row Level Security active, exactly as
/// the API will (task 4).
/// </summary>
[Collection(nameof(PostgresCollection))]
public class RegistrationIntegrationTests
{
    private static readonly PasswordHasher Hasher =
        new(new Argon2Options { MemorySizeKiB = 1024, Iterations = 1, Parallelism = 1 });

    private readonly PostgresFixture _fixture;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero));
    private readonly RecordingAccountNotifier _notifier = new();

    public RegistrationIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private JwtTokenService Tokens => new(new JwtOptions { SigningKey = new string('k', 48) }, _clock);

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static RegisterUserCommand Command(string email, DateOnly? birthDate = null) => new(
        Email: email,
        Password: "a long enough password",
        FullName: "  Ana Pérez ",
        BirthDate: birthDate ?? new DateOnly(1990, 5, 1),
        Country: "co",
        Gender: null,
        AcceptsPrivacyPolicy: true,
        AcceptsTerms: true);

    private RegisterUserHandler RegisterHandler(MiSaldoDbContext db, TestCurrentUser? caller = null)
    {
        var currentUser = caller ?? new TestCurrentUser();
        return new RegisterUserHandler(
            new UserRepository(db),
            new AuthRepository(db),
            new NewUserDataSeeder(db, currentUser),
            new UnitOfWork(db),
            Hasher,
            Tokens,
            _clock,
            _notifier,
            new RegistrationOptions(),
            currentUser);
    }

    private VerifyEmailHandler VerifyHandler(MiSaldoDbContext db) =>
        new(new AuthRepository(db), new UnitOfWork(db), Tokens, _clock);

    private async Task<Guid> RegisterAsync(string email)
    {
        await using var db = _fixture.CreateContext(null);
        var result = await RegisterHandler(db).HandleAsync(Command(email), Ct);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value.UserId;
    }

    // --- 4.1 Registration ---

    [Fact]
    public async Task Registration_creates_the_whole_account_in_one_go()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email.ToUpperInvariant());

        await using var db = _fixture.CreateContext(null);

        var profile = await new UserRepository(db).GetProfileAsync(userId, Ct);
        Assert.NotNull(profile);
        Assert.Equal(email, profile.Email); // stored trimmed and lower-case
        Assert.Equal("Ana Pérez", profile.FullName);
        Assert.Equal("CO", profile.Country);

        var settings = await new UserRepository(db).GetSettingsAsync(userId, Ct);
        Assert.NotNull(settings);
        Assert.Equal("COP", settings.PrimaryCurrency.Code);
        Assert.Equal("es", settings.Language);

        var consent = await db.ConsentRecords.SingleAsync(c => c.UserId == userId, Ct);
        Assert.Equal("1.0", consent.PrivacyPolicyVersion);
        Assert.Equal("1.0", consent.TermsVersion);
        Assert.Equal(_clock.UtcNow, consent.AcceptedAt);

        var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);
        Assert.NotNull(credential);
        Assert.False(credential.EmailVerified);
        Assert.True(Hasher.Verify(credential.PasswordHash, "a long enough password"));
        Assert.DoesNotContain("a long enough password", credential.PasswordHash);
    }

    [Fact]
    public async Task Registration_sends_a_verification_token_that_is_stored_only_by_hash()
    {
        var email = UniqueEmail();
        var userId = await RegisterAsync(email);

        var message = Assert.Single(_notifier.EmailVerifications);
        Assert.Equal(userId, message.UserId);
        Assert.Equal(email, message.Email);

        await using var db = _fixture.CreateContext(null);
        var stored = await new AuthRepository(db).GetOneTimeTokenAsync(
            Tokens.HashToken(message.Token), OneTimeTokenPurpose.EmailVerification, Ct);

        Assert.NotNull(stored);
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(_clock.UtcNow.AddHours(24), stored.ExpiresAt);
        Assert.False(await db.OneTimeTokens.AnyAsync(t => t.TokenHash == message.Token, Ct));
    }

    // RF-CAT-01: the starting categories belong to the new user, and RLS keeps
    // them away from everyone else.
    [Fact]
    public async Task Registration_seeds_the_default_categories_for_the_new_user_only()
    {
        var userId = await RegisterAsync(UniqueEmail());

        await using (var asNewUser = _fixture.CreateContext(userId))
        {
            var names = (await new CategoryRepository(asNewUser).ListAsync(Ct)).Select(c => c.Name).ToList();
            Assert.Equal(DefaultCategories.Names, names);
        }

        await using (var asSomeoneElse = _fixture.CreateContext(Guid.NewGuid()))
        {
            Assert.Empty(await new CategoryRepository(asSomeoneElse).ListAsync(Ct));
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var audited = await db.AuditLogs.CountAsync(
                a => a.UserId == userId && a.EntityType == "ExpenseCategory" && a.Action == "Added", Ct);
            Assert.Equal(DefaultCategories.Names.Count, audited);
        }
    }

    // CP-11: an under-18 visitor is rejected and leaves no data behind.
    [Fact]
    public async Task Cp11_an_underage_registration_is_rejected_without_storing_anything()
    {
        var email = UniqueEmail();
        var seventeen = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime).AddYears(-17);

        await using (var db = _fixture.CreateContext(null))
        {
            var result = await RegisterHandler(db).HandleAsync(Command(email, seventeen), Ct);

            Assert.True(result.IsFailure);
            Assert.Equal(RegisterUserHandler.UnderageCode, result.Error!.Code);
            Assert.Equal(ErrorType.Unprocessable, result.Error.Type);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            Assert.False(await new UserRepository(db).EmailExistsAsync(email, Ct));
        }

        Assert.Empty(_notifier.EmailVerifications);
    }

    [Fact]
    public async Task A_second_registration_with_the_same_email_in_other_case_is_rejected()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        await using (var db = _fixture.CreateContext(null))
        {
            var result = await RegisterHandler(db).HandleAsync(Command(email.ToUpperInvariant()), Ct);

            Assert.True(result.IsFailure);
            Assert.Equal(RegisterUserHandler.UnavailableCode, result.Error!.Code);
            Assert.Equal(ErrorType.Conflict, result.Error.Type);
            Assert.DoesNotContain("exist", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            Assert.Equal(1, await db.UserProfiles.CountAsync(p => p.Email == email, Ct));
        }
    }

    [Fact]
    public async Task A_short_password_is_rejected_and_nothing_is_stored()
    {
        var email = UniqueEmail();

        await using (var db = _fixture.CreateContext(null))
        {
            var result = await RegisterHandler(db).HandleAsync(Command(email) with { Password = "short" }, Ct);

            Assert.True(result.IsFailure);
            Assert.Equal(PasswordPolicy.TooShortCode, result.Error!.Code);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            Assert.False(await new UserRepository(db).EmailExistsAsync(email, Ct));
        }
    }

    [Fact]
    public async Task An_authenticated_caller_cannot_register_another_account()
    {
        await using var db = _fixture.CreateContext(null);

        var result = await RegisterHandler(db, new TestCurrentUser(Guid.NewGuid()))
            .HandleAsync(Command(UniqueEmail()), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
    }

    // The database is the last line against two registrations racing with the same email.
    [Fact]
    public async Task The_database_rejects_two_profiles_with_the_same_email_ignoring_case()
    {
        var email = UniqueEmail();
        var first = await RegisterAsync(email);

        await using var db = _fixture.CreateContext(null);
        var secondUserId = Guid.NewGuid();
        db.Users.Add(User.Register(secondUserId, $"local:{secondUserId}", new DateOnly(1990, 1, 1), new DateOnly(2026, 1, 15)));
        db.UserProfiles.Add(new UserProfile(Guid.NewGuid(), secondUserId, "Other", email.ToUpperInvariant(), "CO"));

        var ex = await Assert.ThrowsAsync<DuplicateKeyException>(() => new UnitOfWork(db).SaveChangesAsync(Ct));
        Assert.Equal("IX_user_profiles_Email_lower", ex.ConstraintName);
        Assert.NotEqual(Guid.Empty, first);
    }

    // --- 4.2 Email verification ---

    [Fact]
    public async Task A_valid_token_marks_the_email_as_verified()
    {
        var userId = await RegisterAsync(UniqueEmail());
        var token = Assert.Single(_notifier.EmailVerifications).Token;

        await using (var db = _fixture.CreateContext(null))
        {
            var result = await VerifyHandler(db).HandleAsync(new VerifyEmailCommand(token), Ct);
            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var credential = await new AuthRepository(db).GetCredentialAsync(userId, Ct);
            Assert.True(credential!.EmailVerified);
        }
    }

    [Fact]
    public async Task A_token_cannot_be_used_twice()
    {
        await RegisterAsync(UniqueEmail());
        var token = Assert.Single(_notifier.EmailVerifications).Token;

        await using (var db = _fixture.CreateContext(null))
        {
            Assert.True((await VerifyHandler(db).HandleAsync(new VerifyEmailCommand(token), Ct)).IsSuccess);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            var second = await VerifyHandler(db).HandleAsync(new VerifyEmailCommand(token), Ct);
            Assert.True(second.IsFailure);
            Assert.Equal(VerifyEmailHandler.InvalidTokenCode, second.Error!.Code);
        }
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var userId = await RegisterAsync(UniqueEmail());
        var token = Assert.Single(_notifier.EmailVerifications).Token;

        _clock.UtcNow = _clock.UtcNow.AddHours(25);

        await using (var db = _fixture.CreateContext(null))
        {
            var result = await VerifyHandler(db).HandleAsync(new VerifyEmailCommand(token), Ct);
            Assert.True(result.IsFailure);
            Assert.Equal(VerifyEmailHandler.InvalidTokenCode, result.Error!.Code);
        }

        await using (var db = _fixture.CreateContext(null))
        {
            Assert.False((await new AuthRepository(db).GetCredentialAsync(userId, Ct))!.EmailVerified);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-token")]
    public async Task An_unknown_or_blank_token_is_rejected(string token)
    {
        await using var db = _fixture.CreateContext(null);

        var result = await VerifyHandler(db).HandleAsync(new VerifyEmailCommand(token), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(VerifyEmailHandler.InvalidTokenCode, result.Error!.Code);
    }
}
