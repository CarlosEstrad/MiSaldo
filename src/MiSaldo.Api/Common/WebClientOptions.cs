using MiSaldo.Infrastructure.Email;
namespace MiSaldo.Api.Common;

/// <summary>Where the web frontend lives; the account emails link to it (R2.1).</summary>
public sealed record FrontendOptions
{
    public const string SectionName = "Frontend";

    /// <summary>Absolute base URL without a trailing slash, for example <c>https://app.misaldo.co</c>.</summary>
    public string? BaseUrl { get; init; }
}

/// <summary>The browser origins allowed to call the API with credentials (R7).</summary>
public sealed record CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>Normalized origins (<c>scheme://host[:port]</c>, lower case). Empty: same origin only.</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
}

/// <summary>The validated email and web-client settings the API starts with.</summary>
public sealed record WebClientSettings(SmtpOptions Smtp, FrontendOptions Frontend, CorsSettings Cors);

/// <summary>
/// Reads and checks the email and web-client settings at startup. Every
/// problem is reported at once, naming the setting but never its value, and the
/// API refuses to start instead of failing later on the first registration
/// (R1.5, R7.2).
/// </summary>
public static class WebClientConfiguration
{
    public static WebClientSettings Read(IConfiguration configuration, bool isProduction)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var smtp = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
        var problems = new List<string>(smtp.Validate(isProduction));

        var baseUrl = configuration[$"{FrontendOptions.SectionName}:BaseUrl"];
        var frontend = new FrontendOptions { BaseUrl = NormalizeBaseUrl(baseUrl, isProduction, problems) };
        if (smtp.IsConfigured && frontend.BaseUrl is null && !problems.Any(p => p.StartsWith("Frontend:", StringComparison.Ordinal)))
        {
            problems.Add("Frontend:BaseUrl is required when Smtp:Host is set: the emails link to the frontend.");
        }

        var origins = configuration.GetSection($"{CorsSettings.SectionName}:AllowedOrigins").Get<string[]>() ?? [];
        var cors = new CorsSettings { AllowedOrigins = NormalizeOrigins(origins, isProduction, problems) };

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The email and web-client settings are not valid:" + Environment.NewLine + "- "
                + string.Join(Environment.NewLine + "- ", problems));
        }

        return new WebClientSettings(smtp, frontend, cors);
    }

    /// <summary>
    /// <c>scheme://host[:port]</c> in lower case when <paramref name="value"/> is
    /// a valid browser origin; null otherwise. A wildcard is never an origin.
    /// </summary>
    public static string? NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('*', StringComparison.Ordinal))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    private static List<string> NormalizeOrigins(IEnumerable<string> values, bool isProduction, List<string> problems)
    {
        var origins = new List<string>();
        var index = 0;
        foreach (var value in values)
        {
            var key = $"{CorsSettings.SectionName}:AllowedOrigins:{index++}";
            if (value?.Trim() == "*" || value?.Contains('*', StringComparison.Ordinal) == true)
            {
                problems.Add($"{key} cannot be a wildcard: with credentials it would expose every session.");
                continue;
            }

            var origin = NormalizeOrigin(value);
            if (origin is null)
            {
                problems.Add($"{key} must be an origin such as https://app.example.com (scheme, host and optional port; no path).");
            }
            else if (isProduction && !origin.StartsWith("https://", StringComparison.Ordinal))
            {
                problems.Add($"{key} must use https in Production.");
            }
            else if (!origins.Contains(origin))
            {
                origins.Add(origin);
            }
        }

        return origins;
    }

    private static string? NormalizeBaseUrl(string? value, bool isProduction, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            problems.Add("Frontend:BaseUrl must be an absolute http or https URL without query or fragment.");
            return null;
        }

        if (isProduction && uri.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add("Frontend:BaseUrl must use https in Production: the links carry single-use tokens.");
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
