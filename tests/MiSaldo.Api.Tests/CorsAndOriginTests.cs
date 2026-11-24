using System.Net;
using System.Net.Http.Json;
using MiSaldo.Api.Auth;
using MiSaldo.Api.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MiSaldo.Api.Tests;

/// <summary>Spec 04, task 7: CORS for the configured frontend origins, without a database.</summary>
public sealed class CorsTests : IDisposable
{
    private const string Frontend = "http://localhost:4200";
    private const string Stranger = "https://evil.example";

    private readonly ApiFactory _factory = ApiFactory.In("Development", new Dictionary<string, string?>
    {
        ["Cors:AllowedOrigins:0"] = Frontend,
        ["Cors:AllowedOrigins:1"] = "https://app.misaldo.test",
    });

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Preflight(string origin, string path = "/api/v1/profile", string method = "POST")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add(HeaderNames.Origin, origin);
        request.Headers.Add(HeaderNames.AccessControlRequestMethod, method);
        request.Headers.Add(HeaderNames.AccessControlRequestHeaders, "authorization,content-type,idempotency-key,x-correlation-id");
        return request;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    private static void AssertNoCorsHeaders(HttpResponseMessage response)
    {
        Assert.Null(Header(response, HeaderNames.AccessControlAllowOrigin));
        Assert.Null(Header(response, HeaderNames.AccessControlAllowCredentials));
        Assert.Null(Header(response, HeaderNames.AccessControlAllowMethods));
        Assert.Null(Header(response, HeaderNames.AccessControlExposeHeaders));
    }

    [Fact]
    public async Task A_preflight_from_an_allowed_origin_gets_permission_without_a_token()
    {
        using var client = _factory.CreateClient();
        using var request = Preflight(Frontend);

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Frontend, Header(response, HeaderNames.AccessControlAllowOrigin));
        Assert.Equal("true", Header(response, HeaderNames.AccessControlAllowCredentials));
        Assert.Equal("600", Header(response, HeaderNames.AccessControlMaxAge));

        var methods = Header(response, HeaderNames.AccessControlAllowMethods)!;
        Assert.Contains("POST", methods, StringComparison.Ordinal);
        var headers = Header(response, HeaderNames.AccessControlAllowHeaders)!.ToLowerInvariant();
        foreach (var expected in new[] { "authorization", "content-type", "idempotency-key", "x-correlation-id" })
        {
            Assert.Contains(expected, headers, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_preflight_from_another_origin_gets_no_permission()
    {
        using var client = _factory.CreateClient();
        using var request = Preflight(Stranger);

        AssertNoCorsHeaders(await client.SendAsync(request, Ct));
    }

    [Fact]
    public async Task A_preflight_for_a_header_the_api_does_not_use_gets_no_header_permission()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/profile");
        request.Headers.Add(HeaderNames.Origin, Frontend);
        request.Headers.Add(HeaderNames.AccessControlRequestMethod, "GET");
        request.Headers.Add(HeaderNames.AccessControlRequestHeaders, "x-something-else");

        var response = await client.SendAsync(request, Ct);

        // The answer lists only the API's headers; the browser then blocks the request.
        var allowed = Header(response, HeaderNames.AccessControlAllowHeaders) ?? string.Empty;
        Assert.DoesNotContain("x-something-else", allowed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_request_from_an_allowed_origin_gets_permission_and_the_exposed_headers()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/profile");
        request.Headers.Add(HeaderNames.Origin, Frontend);

        var response = await client.SendAsync(request, Ct);

        // CORS answers even on a 401, so the frontend can read the error.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Frontend, Header(response, HeaderNames.AccessControlAllowOrigin));
        Assert.Equal("true", Header(response, HeaderNames.AccessControlAllowCredentials));
        var exposed = Header(response, HeaderNames.AccessControlExposeHeaders)!;
        foreach (var expected in new[] { "X-Correlation-ID", "Idempotent-Replayed", "Retry-After", "Location" })
        {
            Assert.Contains(expected, exposed, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_request_from_another_origin_gets_no_cors_headers()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(HeaderNames.Origin, Stranger);

        AssertNoCorsHeaders(await client.SendAsync(request, Ct));
    }

    [Fact]
    public async Task Without_configured_origins_nobody_gets_cors_permission()
    {
        using var factory = ApiFactory.In("Development");
        using var client = factory.CreateClient();
        using var preflight = Preflight(Frontend);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(HeaderNames.Origin, Frontend);

        AssertNoCorsHeaders(await client.SendAsync(preflight, Ct));
        AssertNoCorsHeaders(await client.SendAsync(request, Ct));
    }
}

/// <summary>Spec 04, task 7: which origins the guard of refresh and logout accepts, without a database.</summary>
public class SameOriginGuardRuleTests
{
    private static readonly CorsSettings Cors = new() { AllowedOrigins = ["http://localhost:4200"] };

    private static HttpRequest ApiRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("api.misaldo.test");
        return context.Request;
    }

    [Theory]
    [InlineData("http://localhost:4200", true)]
    [InlineData("HTTP://LOCALHOST:4200", true)]
    [InlineData("https://api.misaldo.test", true)]
    [InlineData("https://api.misaldo.test:443", true)]
    [InlineData("http://api.misaldo.test", false)]
    [InlineData("http://localhost:4201", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    [InlineData("*", false)]
    public void Only_allowed_origins_and_the_api_itself_pass(string origin, bool allowed)
    {
        Assert.Equal(allowed, SameOriginGuardAttribute.IsAllowed(origin, ApiRequest(), Cors));
    }
}

/// <summary>
/// Spec 04, task 7 (🐳): refresh and logout refuse a foreign <c>Origin</c>
/// without touching the session. Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class SameOriginGuardEndpointTests : IDisposable
{
    private const string AuthBase = "/api/v1/auth";
    private const string Frontend = "http://localhost:4200";

    private readonly ApiTestHost _host;

    public SameOriginGuardEndpointTests(ApiPostgresFixture database) =>
        _host = new ApiTestHost(database, configure: builder => builder.UseSetting("Cors:AllowedOrigins:0", Frontend));

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> SignInAsync()
    {
        var email = ApiTestHost.UniqueEmail();
        Assert.Equal(HttpStatusCode.Created,
            (await _host.Client.PostAsJsonAsync($"{AuthBase}/register", ApiTestHost.Registration(email), Ct)).StatusCode);
        var login = await _host.Client.PostAsJsonAsync($"{AuthBase}/login", new { email, password = ApiTestHost.Password }, Ct);
        return SessionCookies.RefreshTokenOf(login);
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string refreshToken, string? origin)
    {
        using var client = _host.CreateClient(handleCookies: false);
        using var request = SessionCookies.PostWithRefreshCookie($"{AuthBase}/{path}", refreshToken);
        if (origin is not null)
        {
            request.Headers.Add(HeaderNames.Origin, origin);
        }

        return await client.SendAsync(request, Ct);
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task A_foreign_origin_is_refused_and_the_session_stays_valid(string path)
    {
        var token = await SignInAsync();

        var response = await PostAsync(path, token, "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SameOriginGuardAttribute.OriginNotAllowedCode,
            (await ApiTestHost.ReadAsync(response)).GetProperty("code").GetString());
        Assert.Null(SessionCookies.RefreshCookieOf(response));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

        // Nothing changed: the same token still renews the session.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync("refresh", token, origin: null)).StatusCode);
    }

    [Fact]
    public async Task An_opaque_null_origin_is_refused()
    {
        var token = await SignInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync("refresh", token, "null")).StatusCode);
    }

    [Fact]
    public async Task The_frontend_origin_and_the_api_origin_are_accepted()
    {
        var token = await SignInAsync();

        var fromFrontend = await PostAsync("refresh", token, Frontend);
        Assert.Equal(HttpStatusCode.OK, fromFrontend.StatusCode);
        Assert.Equal(Frontend, fromFrontend.Headers.GetValues(HeaderNames.AccessControlAllowOrigin).Single());

        var fromApi = await PostAsync("refresh", SessionCookies.RefreshTokenOf(fromFrontend), "https://localhost");
        Assert.Equal(HttpStatusCode.OK, fromApi.StatusCode);

        var logout = await PostAsync("logout", SessionCookies.RefreshTokenOf(fromApi), Frontend);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.True(SessionCookies.DeletesRefreshCookie(logout));
    }

    [Fact]
    public async Task A_request_without_origin_is_accepted()
    {
        var token = await SignInAsync();

        Assert.Equal(HttpStatusCode.OK, (await PostAsync("refresh", token, origin: null)).StatusCode);
    }
}
