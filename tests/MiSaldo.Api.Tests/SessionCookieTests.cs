using System.Net;
using System.Net.Http.Json;
using MiSaldo.Api.Auth;
using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MiSaldo.Api.Tests;

/// <summary>Spec 04, task 6: the refresh cookie's attributes, without a database.</summary>
public class RefreshCookieTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 15, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;

        public DateOnly Today(TimeZoneInfo timeZone) => DateOnly.FromDateTime(Now.UtcDateTime);
    }

    private static DefaultHttpContext NewContext() => new()
    {
        RequestServices = new ServiceCollection().AddSingleton<IClock>(new FixedClock()).BuildServiceProvider(),
    };

    private static SetCookieHeaderValue SetCookieOf(HttpContext context) =>
        Assert.Single(SetCookieHeaderValue.ParseList(context.Response.Headers.SetCookie.Select(v => v ?? string.Empty).ToList()));

    [Fact]
    public void The_cookie_is_http_only_secure_strict_scoped_to_auth_and_lives_as_long_as_the_session()
    {
        var context = NewContext();

        RefreshCookie.Write(context.Response, "the-token", Now.AddDays(14));

        var cookie = SetCookieOf(context);
        Assert.Equal("__Secure-misaldo-refresh", cookie.Name.ToString());
        Assert.Equal("the-token", cookie.Value.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/v1/auth", cookie.Path.ToString());
        Assert.Equal(TimeSpan.FromDays(14), cookie.MaxAge);
        Assert.Equal(Now.AddDays(14), cookie.Expires);
        Assert.True(cookie.Domain.Length == 0, "The cookie must stay on the API host.");
    }

    [Fact]
    public void Deleting_keeps_the_same_attributes_and_expires_the_cookie()
    {
        var context = NewContext();

        RefreshCookie.Delete(context.Response);

        var cookie = SetCookieOf(context);
        Assert.Equal(RefreshCookie.Name, cookie.Name.ToString());
        Assert.Equal(0, cookie.Value.Length);
        Assert.True(cookie.Expires < Now);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal(RefreshCookie.Path, cookie.Path.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("abc", "abc")]
    public void Reading_returns_the_token_or_null(string? value, string? expected)
    {
        var context = NewContext();
        if (value is not null)
        {
            context.Request.Headers.Cookie = $"{RefreshCookie.Name}={value}";
        }

        Assert.Equal(expected, RefreshCookie.Read(context.Request));
    }

    [Fact]
    public void The_session_response_has_no_refresh_token_and_hides_the_access_token_when_printed()
    {
        var tokens = new AuthTokens(Guid.NewGuid(), "access", Now.AddMinutes(30), "refresh", Now.AddDays(14), true);

        var response = SessionResponse.From(tokens);

        Assert.DoesNotContain(typeof(SessionResponse).GetProperties(), p => p.Name.Contains("Refresh", StringComparison.Ordinal));
        Assert.Equal(Now.AddDays(14), response.SessionExpiresAt);
        Assert.DoesNotContain("access", response.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>
/// Spec 04, task 6 (🐳): the session over HTTP with the refresh token only in
/// the cookie, against a real PostgreSQL. Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class SessionCookieEndpointTests : IDisposable
{
    private const string AuthBase = "/api/v1/auth";

    private readonly ApiTestHost _host;

    public SessionCookieEndpointTests(ApiPostgresFixture database) => _host = new ApiTestHost(database);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> RegisterAsync()
    {
        var email = ApiTestHost.UniqueEmail();
        Assert.Equal(HttpStatusCode.Created,
            (await _host.Client.PostAsJsonAsync($"{AuthBase}/register", ApiTestHost.Registration(email), Ct)).StatusCode);
        return email;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync($"{AuthBase}/login", new { email, password = ApiTestHost.Password }, Ct);

    [Fact]
    public async Task Login_sets_the_cookie_and_the_body_has_no_refresh_token()
    {
        var email = await RegisterAsync();
        var before = DateTimeOffset.UtcNow;

        var response = await LoginAsync(_host.Client, email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = SessionCookies.RefreshCookieOf(response);
        Assert.NotNull(cookie);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/v1/auth", cookie.Path.ToString());
        Assert.NotNull(cookie.MaxAge);
        Assert.InRange(cookie.MaxAge.Value, TimeSpan.FromDays(14) - TimeSpan.FromMinutes(1), TimeSpan.FromDays(14));

        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain(cookie.Value.ToString(), raw, StringComparison.Ordinal);
        var body = await ApiTestHost.ReadAsync(response);
        Assert.False(body.TryGetProperty("refreshToken", out _));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.NotEqual(Guid.Empty, body.GetProperty("userId").GetGuid());
        Assert.False(body.GetProperty("emailVerified").GetBoolean());
        Assert.True(body.GetProperty("accessTokenExpiresAt").GetDateTimeOffset() > before);
        Assert.InRange(body.GetProperty("sessionExpiresAt").GetDateTimeOffset(),
            before.AddDays(14).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(14).AddMinutes(1));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Login_refresh_and_logout_work_with_the_cookie_alone()
    {
        var email = await RegisterAsync();
        using var browser = _host.CreateClient();

        var login = await LoginAsync(browser, email);
        var first = SessionCookies.RefreshTokenOf(login);

        // No body: the client only holds the cookie.
        var refresh = await browser.PostAsync($"{AuthBase}/refresh", null, Ct);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var second = SessionCookies.RefreshTokenOf(refresh);
        Assert.NotEqual(first, second);
        Assert.Contains("no-store", refresh.Headers.CacheControl?.ToString());
        Assert.False((await ApiTestHost.ReadAsync(refresh)).TryGetProperty("refreshToken", out _));

        // The browser now holds the rotated cookie, so a second refresh works too.
        var again = await browser.PostAsync($"{AuthBase}/refresh", null, Ct);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        var logout = await browser.PostAsync($"{AuthBase}/logout", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.True(SessionCookies.DeletesRefreshCookie(logout));
        Assert.Contains("no-store", logout.Headers.CacheControl?.ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsync($"{AuthBase}/refresh", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", SessionCookies.RefreshTokenOf(again))).StatusCode);
    }

    [Fact]
    public async Task A_refresh_without_a_cookie_is_401_and_clears_the_cookie()
    {
        var response = await _host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.invalid_refresh_token", (await ApiTestHost.ReadAsync(response)).GetProperty("code").GetString());
        Assert.True(SessionCookies.DeletesRefreshCookie(response));
    }

    [Fact]
    public async Task A_refresh_token_sent_in_the_body_is_ignored()
    {
        var email = await RegisterAsync();
        var token = SessionCookies.RefreshTokenOf(await LoginAsync(_host.Client, email));

        using var noCookies = _host.CreateClient(handleCookies: false);
        var response = await noCookies.PostAsJsonAsync($"{AuthBase}/refresh", new { refreshToken = token }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The session itself is still valid through the cookie.
        Assert.Equal(HttpStatusCode.OK, (await _host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", token)).StatusCode);
    }

    [Fact]
    public async Task A_reused_cookie_is_rejected_cleared_and_closes_the_session()
    {
        var email = await RegisterAsync();
        var first = SessionCookies.RefreshTokenOf(await LoginAsync(_host.Client, email));

        var rotated = await _host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", first);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = SessionCookies.RefreshTokenOf(rotated);

        // Someone replays the old cookie.
        var replay = await _host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", first);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.True(SessionCookies.DeletesRefreshCookie(replay));

        // The reuse revoked the session that replaced it.
        var afterReuse = await _host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", second);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
        Assert.True(SessionCookies.DeletesRefreshCookie(afterReuse));
    }

    [Fact]
    public async Task A_password_reset_clears_the_cookie_and_closes_the_sessions()
    {
        var email = await RegisterAsync();
        using var browser = _host.CreateClient();
        var token = SessionCookies.RefreshTokenOf(await LoginAsync(browser, email));

        Assert.Equal(HttpStatusCode.Accepted,
            (await browser.PostAsJsonAsync($"{AuthBase}/forgot-password", new { email }, Ct)).StatusCode);
        var resetToken = _host.Notifier.LatestPasswordReset(email)!.Token;

        var reset = await browser.PostAsJsonAsync(
            $"{AuthBase}/reset-password", new { token = resetToken, newPassword = "a brand new password" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.True(SessionCookies.DeletesRefreshCookie(reset));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", token)).StatusCode);
    }

    [Fact]
    public async Task A_failed_reset_leaves_the_cookie_alone()
    {
        var email = await RegisterAsync();
        var token = SessionCookies.RefreshTokenOf(await LoginAsync(_host.Client, email));

        var reset = await _host.Client.PostAsJsonAsync(
            $"{AuthBase}/reset-password", new { token = "not-a-real-token", newPassword = "a brand new password" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Null(SessionCookies.RefreshCookieOf(reset));
        Assert.Equal(HttpStatusCode.OK, (await _host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", token)).StatusCode);
    }
}
