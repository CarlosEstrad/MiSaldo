using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MiSaldo.Api.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Task 8 (🐳): the authentication endpoints against a real PostgreSQL, through
/// the HTTP pipeline: registration, email verification, login, refresh, logout
/// and password recovery. Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class AuthEndpointsTests : IDisposable
{
    private const string Password = "a long enough password";
    private const string AuthBase = $"/{ApiRoutes.V1}/auth";
    private const int MaxFailedAttempts = 3;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests(ApiPostgresFixture database)
    {
        _factory = ApiFactory.With(ApiFactory.TestSigningKey, database.AppConnectionString)
            .WithWebHostBuilder(builder =>
            {
                // Cheap hashing and a high request budget keep the suite fast; the lockout is lowered to test it.
                builder.UseSetting("Argon2:MemorySizeKiB", "1024");
                builder.UseSetting("Argon2:Iterations", "1");
                builder.UseSetting("Argon2:Parallelism", "1");
                builder.UseSetting("Authentication:MaxFailedLoginAttempts", MaxFailedAttempts.ToString());
                builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");
                builder.ConfigureServices(services => services
                    .AddControllers()
                    .AddApplicationPart(typeof(WhoAmITestController).Assembly));
            });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = SessionCookies.BaseAddress });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private InMemoryAccountNotifier Notifier => _factory.Services.GetRequiredService<InMemoryAccountNotifier>();
    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static object Registration(string email, DateOnly? birthDate = null, string password = Password) => new
    {
        email,
        password,
        fullName = "Ana Perez",
        birthDate = birthDate ?? new DateOnly(1990, 5, 1),
        country = "CO",
        acceptsPrivacyPolicy = true,
        acceptsTerms = true,
    };

    private Task<HttpResponseMessage> PostAsync(string path, object body) =>
        _client.PostAsJsonAsync($"{AuthBase}/{path}", body, Ct);

    private async Task<string> RegisterAsync(string? email = null)
    {
        email ??= UniqueEmail();
        var response = await PostAsync("register", Registration(email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private async Task VerifyAsync(string email)
    {
        var token = Notifier.LatestEmailVerification(email)?.Token;
        Assert.NotNull(token);
        var response = await PostAsync("verify-email", new { token });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed record Login(JsonElement Body, string RefreshToken);

    private async Task<Login> LoginAsync(string email, string password = Password)
    {
        var response = await PostAsync("login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new Login(await ReadAsync(response), SessionCookies.RefreshTokenOf(response));
    }

    /// <summary>Refresh or logout carrying exactly this refresh token in the cookie, from a client without cookies.</summary>
    private async Task<HttpResponseMessage> WithCookieAsync(string path, string? refreshToken)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = SessionCookies.BaseAddress,
            HandleCookies = false,
        });
        using var request = SessionCookies.PostWithRefreshCookie($"{AuthBase}/{path}", refreshToken);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> WhoAmIAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{ApiRoutes.V1}/test-whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task The_whole_session_flow_works_over_http()
    {
        var email = await RegisterAsync();

        // Before verifying, login works but the token says the email is not verified (RF-AUT-03).
        var unverified = await LoginAsync(email);
        Assert.False(unverified.Body.GetProperty("emailVerified").GetBoolean());

        await VerifyAsync(email);
        var tokens = await LoginAsync(email);
        Assert.True(tokens.Body.GetProperty("emailVerified").GetBoolean());
        var userId = tokens.Body.GetProperty("userId").GetGuid();

        // The access token opens a private route and the user comes from its sub claim.
        var whoAmI = await WhoAmIAsync(tokens.Body.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, whoAmI.StatusCode);
        Assert.Equal(userId, (await ReadAsync(whoAmI)).GetProperty("userId").GetGuid());

        // Refresh rotates: the new cookie works, the old refresh token does not.
        var oldRefresh = tokens.RefreshToken;
        var refreshed = await WithCookieAsync("refresh", oldRefresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var newRefresh = SessionCookies.RefreshTokenOf(refreshed);
        Assert.NotEqual(oldRefresh, newRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithCookieAsync("refresh", oldRefresh)).StatusCode);

        // Reusing the rotated token also closed the session that replaced it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithCookieAsync("refresh", newRefresh)).StatusCode);

        // Logout revokes the refresh token.
        var second = await LoginAsync(email);
        var logout = await WithCookieAsync("logout", second.RefreshToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithCookieAsync("refresh", second.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Registration_answers_201_with_the_new_user_id()
    {
        var response = await PostAsync("register", Registration(UniqueEmail()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(Guid.Empty, (await ReadAsync(response)).GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task A_visitor_under_18_is_rejected_and_leaves_no_account()
    {
        var email = UniqueEmail();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await PostAsync("register", Registration(email, today.AddYears(-10)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("registration.underage", (await ReadAsync(response)).GetProperty("code").GetString());
        Assert.Null(Notifier.LatestEmailVerification(email));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("login", new { email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task A_duplicate_email_is_rejected_without_saying_it_exists()
    {
        var email = await RegisterAsync();
        var response = await PostAsync("register", Registration(email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("registration.unavailable", body.GetProperty("code").GetString());
        Assert.DoesNotContain("already", body.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_short_password_is_rejected_with_400()
    {
        var response = await PostAsync("register", Registration(UniqueEmail(), password: "short"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("password.too_short", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_body_with_missing_fields_is_rejected_before_the_use_case_runs()
    {
        var response = await PostAsync("register", new { email = "someone@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ReadAsync(response);
        Assert.Equal("request.invalid", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("errors", out _));
        Assert.True(body.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task An_email_verification_token_works_once()
    {
        var email = await RegisterAsync();
        var token = Notifier.LatestEmailVerification(email)!.Token;

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync("verify-email", new { token })).StatusCode);
        var again = await PostAsync("verify-email", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
        Assert.Equal("verification.invalid_token", (await ReadAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_made_up_verification_token_is_rejected()
    {
        var response = await PostAsync("verify-email", new { token = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        var email = await RegisterAsync();
        var wrongPassword = await PostAsync("login", new { email, password = "this is not the password" });
        var unknownEmail = await PostAsync("login", new { email = UniqueEmail(), password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var first = await ReadAsync(wrongPassword);
        var second = await ReadAsync(unknownEmail);
        Assert.Equal("auth.invalid_credentials", first.GetProperty("code").GetString());
        Assert.Equal(first.GetProperty("code").GetString(), second.GetProperty("code").GetString());
        Assert.Equal(first.GetProperty("detail").GetString(), second.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_account_locks_after_too_many_failed_logins_even_for_the_right_password()
    {
        var email = await RegisterAsync();
        for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await PostAsync("login", new { email, password = "wrong password here" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("login", new { email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task Token_responses_are_not_cacheable()
    {
        var email = await RegisterAsync();
        var response = await PostAsync("login", new { email, password = Password });
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Password_recovery_answers_the_same_for_known_and_unknown_emails()
    {
        var email = await RegisterAsync();
        var known = await PostAsync("forgot-password", new { email });
        var unknown = await PostAsync("forgot-password", new { email = UniqueEmail() });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(Ct), await unknown.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Resetting_the_password_changes_it_closes_sessions_and_the_token_works_once()
    {
        var email = await RegisterAsync();
        var session = await LoginAsync(email);
        await PostAsync("forgot-password", new { email });
        var token = Notifier.LatestPasswordReset(email)!.Token;
        const string newPassword = "a brand new password";

        var reset = await PostAsync("reset-password", new { token, newPassword });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // The old session and the old password stop working; the new one works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithCookieAsync("refresh", session.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync("login", new { email, password = Password })).StatusCode);
        await LoginAsync(email, newPassword);

        var reused = await PostAsync("reset-password", new { token, newPassword = "yet another password" });
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal("reset.invalid_token", (await ReadAsync(reused)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_reset_with_a_weak_password_is_rejected()
    {
        var email = await RegisterAsync();
        await PostAsync("forgot-password", new { email });
        var token = Notifier.LatestPasswordReset(email)!.Token;

        var response = await PostAsync("reset-password", new { token, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logging_out_with_an_unknown_token_still_succeeds()
    {
        var response = await WithCookieAsync("logout", "never-issued");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
