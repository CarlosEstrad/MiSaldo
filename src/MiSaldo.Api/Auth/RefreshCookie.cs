using MiSaldo.Application.Abstractions;

namespace MiSaldo.Api.Auth;

/// <summary>
/// The refresh token travels only in this cookie, never in a response body, so
/// the page's JavaScript cannot read it (RNF-SEG-03). It is <c>HttpOnly</c>,
/// <c>Secure</c> (required anyway by the <c>__Secure-</c> prefix),
/// <c>SameSite=Strict</c>, limited to the authentication routes, and lives as
/// long as the session it carries (design E8).
/// </summary>
public static class RefreshCookie
{
    public const string Name = "__Secure-misaldo-refresh";

    /// <summary>Only the authentication routes receive the cookie.</summary>
    public const string Path = $"/{ApiRoutes.V1}/auth";

    public static void Write(HttpResponse response, string token, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var now = response.HttpContext.RequestServices.GetRequiredService<IClock>().UtcNow;
        var options = BaseOptions();
        options.Expires = expiresAt;
        options.MaxAge = expiresAt > now ? expiresAt - now : TimeSpan.Zero;
        response.Cookies.Append(Name, token, options);
    }

    /// <summary>The refresh token sent by the client, or null when the cookie is missing or empty.</summary>
    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = request.Cookies[Name];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Tells the browser to drop the cookie. The attributes must match the ones it was set with.</summary>
    public static void Delete(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(Name, BaseOptions());
    }

    private static CookieOptions BaseOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        IsEssential = true,
    };
}
