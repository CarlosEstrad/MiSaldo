using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
namespace MiSaldo.Infrastructure.Email;

/// <summary>The account emails the system sends.</summary>
public enum AccountEmailKind
{
    EmailVerification,
    PasswordReset,
}

/// <summary>A ready-to-send email: subject, plain text and HTML (R2.2).</summary>
public sealed record AccountEmail(string Subject, string Text, string Html)
{
    // The bodies carry a single-use link: never print them.
    public override string ToString() => Redaction.Of(nameof(AccountEmail));
}

/// <summary>
/// Builds the account emails from templates embedded in this assembly, one per
/// email, language and format, so no text lives in the code (R2.4, RF-CFG-03).
/// The link points to the frontend page that consumes the token (R2.1), and the
/// email says how long it stays valid (R2.2). Values placed in the HTML are
/// encoded.
/// </summary>
public sealed class AccountEmailComposer
{
    public const string FallbackLanguage = "es";

    /// <summary>The languages that have a full set of templates (RF-CFG-03: Spanish and English in F1).</summary>
    public static IReadOnlyList<string> Languages { get; } = ["es", "en"];

    private static readonly ConcurrentDictionary<string, string> Resources = new(StringComparer.Ordinal);

    private readonly string _frontendBaseUrl;
    private readonly IClock _clock;

    public AccountEmailComposer(string frontendBaseUrl, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontendBaseUrl);
        ArgumentNullException.ThrowIfNull(clock);
        _frontendBaseUrl = frontendBaseUrl.TrimEnd('/');
        _clock = clock;
    }

    public AccountEmail Compose(AccountEmailKind kind, AccountTokenMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var language = Languages.Contains(message.Language) ? message.Language : FallbackLanguage;
        var template = TemplateName(kind);
        var strings = Strings(language);

        var link = $"{_frontendBaseUrl}/{template}?token={Uri.EscapeDataString(message.Token)}";
        var validFor = ValidFor(message.ExpiresAt - _clock.UtcNow, strings);
        var subject = strings[$"{template}.subject"];

        var text = Fill(Load($"{template}.{language}.txt"), link, validFor, subject, encode: false);
        var html = Fill(Load($"{template}.{language}.html"), link, validFor, subject, encode: true);
        return new AccountEmail(subject, text, html);
    }

    /// <summary>The path of the frontend page, which is also the template name.</summary>
    public static string TemplateName(AccountEmailKind kind) => kind switch
    {
        AccountEmailKind.EmailVerification => "verify-email",
        AccountEmailKind.PasswordReset => "reset-password",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown account email."),
    };

    /// <summary>
    /// "24 horas", "1 hour", "30 minutos": whole hours from one hour up, whole
    /// minutes below, never less than one minute.
    /// </summary>
    private static string ValidFor(TimeSpan remaining, IReadOnlyDictionary<string, string> strings)
    {
        // Whole minutes first: the token is created a moment before the email, so a
        // one-hour link has a few milliseconds less than an hour left and must still say "1 hour".
        var minutes = Math.Max(1, (int)Math.Round(remaining.TotalMinutes, MidpointRounding.AwayFromZero));
        if (minutes >= 60)
        {
            var hours = (int)Math.Round(minutes / 60.0, MidpointRounding.AwayFromZero);
            return $"{hours.ToString(CultureInfo.InvariantCulture)} {strings[hours == 1 ? "hour" : "hours"]}";
        }

        return $"{minutes.ToString(CultureInfo.InvariantCulture)} {strings[minutes == 1 ? "minute" : "minutes"]}";
    }

    private static string Fill(string template, string link, string validFor, string subject, bool encode)
    {
        string Value(string value) => encode ? WebUtility.HtmlEncode(value) : value;

        return template
            .Replace("{{Link}}", Value(link), StringComparison.Ordinal)
            .Replace("{{ValidFor}}", Value(validFor), StringComparison.Ordinal)
            .Replace("{{Subject}}", Value(subject), StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Strings(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Load($"strings.{language}.json"))
        ?? throw new InvalidOperationException($"The email strings for '{language}' are empty.");

    /// <summary>Reads an embedded template once and keeps it; a missing one is a build mistake.</summary>
    internal static string Load(string fileName) => Resources.GetOrAdd(fileName, name =>
    {
        var resource = $"MiSaldo.Email.Templates.{name}";
        using var stream = typeof(AccountEmailComposer).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The email template '{resource}' is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
