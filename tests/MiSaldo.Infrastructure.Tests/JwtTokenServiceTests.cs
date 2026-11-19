using System.Text;
using MiSaldo.Infrastructure.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

public class JwtTokenServiceTests
{
    private static JwtOptions Options() => new()
    {
        SigningKey = new string('k', 48),
        Issuer = "test-issuer",
        Audience = "test-audience",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 14,
    };

    private static TokenValidationParameters Validation(JwtOptions options, string? key = null) => new()
    {
        ValidIssuer = options.Issuer,
        ValidAudience = options.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? options.SigningKey)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.Zero,
    };

    private static JwtTokenService NewService(JwtOptions options, FakeClock clock) => new(options, clock);

    // --- Access tokens ---

    [Fact]
    public async Task Access_token_is_valid_and_carries_the_user_id_and_verification_flag()
    {
        var options = Options();
        var userId = Guid.NewGuid();
        var service = NewService(options, new FakeClock());

        var token = service.CreateAccessToken(userId, emailVerified: true);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Validation(options));

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(userId.ToString(), result.Claims[JwtClaims.Subject]);
        Assert.Equal("true", result.Claims[JwtClaims.EmailVerified].ToString()!.ToLowerInvariant());
    }

    [Fact]
    public async Task Unverified_email_is_reflected_in_the_token()
    {
        var options = Options();
        var service = NewService(options, new FakeClock());

        var token = service.CreateAccessToken(Guid.NewGuid(), emailVerified: false);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Validation(options));

        Assert.True(result.IsValid);
        Assert.Equal("false", result.Claims[JwtClaims.EmailVerified].ToString()!.ToLowerInvariant());
    }

    [Fact]
    public void Access_token_expires_after_the_configured_minutes()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var service = NewService(Options(), clock);

        var token = service.CreateAccessToken(Guid.NewGuid(), true);

        Assert.Equal(clock.UtcNow.AddMinutes(30), token.ExpiresAt);
    }

    [Fact]
    public void Access_token_is_signed_with_hmac_sha256()
    {
        var service = NewService(Options(), new FakeClock());

        var token = service.CreateAccessToken(Guid.NewGuid(), true);

        Assert.Equal("HS256", new JsonWebToken(token.Value).Alg);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        var options = Options();
        var service = NewService(options, new FakeClock());
        var token = service.CreateAccessToken(Guid.NewGuid(), true);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            token.Value, Validation(options, key: new string('x', 48)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var options = Options();
        var service = NewService(options, new FakeClock(DateTimeOffset.UtcNow.AddHours(-2))); // 30 min life, issued 2h ago
        var token = service.CreateAccessToken(Guid.NewGuid(), true);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Validation(options));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_rejected()
    {
        var options = Options();
        var service = NewService(options, new FakeClock());
        var token = service.CreateAccessToken(Guid.NewGuid(), true);

        var other = Validation(options);
        other.ValidAudience = "someone-else";
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, other);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Each_access_token_is_unique()
    {
        var service = NewService(Options(), new FakeClock());
        var userId = Guid.NewGuid();

        Assert.NotEqual(
            service.CreateAccessToken(userId, true).Value,
            service.CreateAccessToken(userId, true).Value);
    }

    // --- Opaque tokens ---

    [Fact]
    public void Opaque_tokens_are_unique_url_safe_and_hash_to_their_stored_value()
    {
        var service = NewService(Options(), new FakeClock());

        var tokens = Enumerable.Range(0, 50).Select(_ => service.CreateOpaqueToken()).ToList();

        Assert.Equal(50, tokens.Select(t => t.Value).Distinct().Count());
        Assert.All(tokens, t =>
        {
            Assert.Equal(service.HashToken(t.Value), t.Hash);
            Assert.NotEqual(t.Value, t.Hash);
            Assert.Equal(64, t.Hash.Length);
            Assert.Matches("^[0-9a-f]{64}$", t.Hash);
            Assert.DoesNotContain('+', t.Value);
            Assert.DoesNotContain('/', t.Value);
            Assert.DoesNotContain('=', t.Value);
        });
    }

    [Fact]
    public void Hashing_is_deterministic_and_distinguishes_tokens()
    {
        var service = NewService(Options(), new FakeClock());

        Assert.Equal(service.HashToken("abc"), service.HashToken("abc"));
        Assert.NotEqual(service.HashToken("abc"), service.HashToken("abd"));
    }

    [Fact]
    public void Refresh_token_lifetime_comes_from_the_options()
    {
        Assert.Equal(TimeSpan.FromDays(14), NewService(Options(), new FakeClock()).RefreshTokenLifetime);
    }

    // --- Options ---

    [Fact]
    public void A_short_signing_key_is_rejected()
    {
        var options = Options();
        options.SigningKey = new string('k', 31); // one byte short of 256 bits

        Assert.Throws<ArgumentException>(() => NewService(options, new FakeClock()));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(61)]
    public void An_access_token_lifetime_outside_15_to_60_minutes_is_rejected(int minutes)
    {
        var options = Options();
        options.AccessTokenMinutes = minutes;

        Assert.Throws<ArgumentOutOfRangeException>(() => NewService(options, new FakeClock()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void A_refresh_token_lifetime_outside_1_to_90_days_is_rejected(int days)
    {
        var options = Options();
        options.RefreshTokenDays = days;

        Assert.Throws<ArgumentOutOfRangeException>(() => NewService(options, new FakeClock()));
    }

    [Fact]
    public void Blank_issuer_or_audience_is_rejected()
    {
        var noIssuer = Options();
        noIssuer.Issuer = " ";
        var noAudience = Options();
        noAudience.Audience = string.Empty;

        Assert.Throws<ArgumentException>(() => NewService(noIssuer, new FakeClock()));
        Assert.Throws<ArgumentException>(() => NewService(noAudience, new FakeClock()));
    }
}
