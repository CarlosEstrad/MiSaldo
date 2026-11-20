using MiSaldo.Api.Auth;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MiSaldo.Api.Tests;

/// <summary>Reads and sends the refresh cookie by hand, for tests that need an exact token.</summary>
public static class SessionCookies
{
    /// <summary>The base address of test clients: <c>Secure</c> cookies only travel over https.</summary>
    public static readonly Uri BaseAddress = new("https://localhost");

    /// <summary>The refresh cookie the response sets or deletes, or null when it does not touch it.</summary>
    public static SetCookieHeaderValue? RefreshCookieOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Headers.TryGetValues(HeaderNames.SetCookie, out var values))
        {
            return null;
        }

        return SetCookieHeaderValue.ParseList(values.ToList())
            .SingleOrDefault(c => c.Name.Equals(RefreshCookie.Name, StringComparison.Ordinal));
    }

    /// <summary>The refresh token the response sets; fails the test when there is none.</summary>
    public static string RefreshTokenOf(HttpResponseMessage response)
    {
        var cookie = RefreshCookieOf(response);
        Assert.NotNull(cookie);
        Assert.False(cookie.Value.Length == 0, "The response deletes the refresh cookie instead of setting it.");
        return cookie.Value.ToString();
    }

    /// <summary>True when the response tells the browser to drop the refresh cookie.</summary>
    public static bool DeletesRefreshCookie(HttpResponseMessage response) =>
        RefreshCookieOf(response) is { } cookie
        && cookie.Value.Length == 0
        && cookie.Expires is { } expires && expires < DateTimeOffset.UtcNow;

    /// <summary>A POST that carries exactly the given refresh token in the cookie and no body.</summary>
    public static HttpRequestMessage PostWithRefreshCookie(string path, string? refreshToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (refreshToken is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, $"{RefreshCookie.Name}={refreshToken}");
        }

        return request;
    }
}
