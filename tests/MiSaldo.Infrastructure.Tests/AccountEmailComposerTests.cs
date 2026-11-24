using MiSaldo.Application.Abstractions;
using MiSaldo.Infrastructure.Email;
using Xunit;
namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Spec 04, task 3: the account emails are built from embedded templates, in the
/// user's language, with a working link to the frontend and the time the link
/// stays valid. No Docker needed.
/// </summary>
public class AccountEmailComposerTests
{
    private const string Frontend = "https://app.misaldo.test";
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 15, 0, 0, TimeSpan.Zero);

    private static AccountEmailComposer Composer(string baseUrl = Frontend) => new(baseUrl, new FakeClock(Now));

    private static AccountTokenMessage Message(string language, TimeSpan validFor, string token = "tok_abc-123") =>
        new(Guid.NewGuid(), "ana@example.com", token, language, Now + validFor);

    [Theory]
    [InlineData(AccountEmailKind.EmailVerification, "es", "Confirma tu correo en MiSaldo", "24 horas")]
    [InlineData(AccountEmailKind.EmailVerification, "en", "Confirm your email for MiSaldo", "24 hours")]
    [InlineData(AccountEmailKind.PasswordReset, "es", "Restablece tu contraseña de MiSaldo", "1 hora")]
    [InlineData(AccountEmailKind.PasswordReset, "en", "Reset your MiSaldo password", "1 hour")]
    public void Each_email_comes_in_the_users_language_with_its_link_and_validity(
        AccountEmailKind kind, string language, string subject, string validFor)
    {
        var lifetime = kind == AccountEmailKind.EmailVerification ? TimeSpan.FromHours(24) : TimeSpan.FromHours(1);
        var email = Composer().Compose(kind, Message(language, lifetime));
        var link = $"{Frontend}/{AccountEmailComposer.TemplateName(kind)}?token=tok_abc-123";

        Assert.Equal(subject, email.Subject);
        Assert.Contains(link, email.Text, StringComparison.Ordinal);
        Assert.Contains($"href=\"{link}\"", email.Html, StringComparison.Ordinal);
        Assert.Contains(validFor, email.Text, StringComparison.Ordinal);
        Assert.Contains(validFor, email.Html, StringComparison.Ordinal);
        Assert.Contains($"<html lang=\"{language}\">", email.Html, StringComparison.Ordinal);
        Assert.Contains($"<title>{System.Net.WebUtility.HtmlEncode(subject)}</title>", email.Html, StringComparison.Ordinal);
    }

    // The token is created a moment before the email is composed, so a little less
    // than the full lifetime is left; the email must still state the round figure.
    [Theory]
    [InlineData("en", 3600 - 1, "1 hour")]
    [InlineData("es", 3600 - 1, "1 hora")]
    [InlineData("en", 24 * 3600 - 1, "24 hours")]
    [InlineData("en", 59 * 60 + 29, "59 minutes")]
    [InlineData("en", 89 * 60 + 29, "1 hour")]
    [InlineData("en", 90 * 60, "2 hours")]
    [InlineData("en", 20, "1 minute")]
    public void The_validity_is_stated_in_whole_minutes_or_hours(string language, int secondsLeft, string expected)
    {
        var email = Composer().Compose(AccountEmailKind.PasswordReset, Message(language, TimeSpan.FromSeconds(secondsLeft)));
        var phrase = language == "es" ? "vence en" : "expires in";
        Assert.Contains($"{phrase} {expected}.", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_links_point_to_the_frontend_pages_that_consume_the_tokens()
    {
        Assert.Equal("verify-email", AccountEmailComposer.TemplateName(AccountEmailKind.EmailVerification));
        Assert.Equal("reset-password", AccountEmailComposer.TemplateName(AccountEmailKind.PasswordReset));
    }

    [Theory]
    [InlineData("es", "ignora este correo")]
    [InlineData("en", "ignore this email")]
    public void The_reset_email_says_it_can_be_ignored(string language, string phrase)
    {
        var email = Composer().Compose(AccountEmailKind.PasswordReset, Message(language, TimeSpan.FromHours(1)));
        Assert.Contains(phrase, email.Text, StringComparison.Ordinal);
        Assert.Contains(phrase, email.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_token_is_escaped_in_the_link()
    {
        var email = Composer().Compose(AccountEmailKind.EmailVerification, Message("es", TimeSpan.FromHours(24), token: "a+b/c=d"));
        Assert.Contains("?token=a%2Bb%2Fc%3Dd", email.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("a+b/c=d", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_are_html_encoded_in_the_html_version()
    {
        var email = Composer("https://app.misaldo.test/a&b").Compose(AccountEmailKind.EmailVerification, Message("en", TimeSpan.FromHours(24)));

        Assert.Contains("https://app.misaldo.test/a&amp;b/verify-email", email.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("/a&b/", email.Html, StringComparison.Ordinal);
        Assert.Contains("https://app.misaldo.test/a&b/verify-email", email.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_trailing_slash_in_the_frontend_url_does_not_double_the_separator()
    {
        var email = Composer(Frontend + "/").Compose(AccountEmailKind.EmailVerification, Message("es", TimeSpan.FromHours(24)));
        Assert.Contains($"{Frontend}/verify-email?token=", email.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    public void An_unknown_language_falls_back_to_spanish(string language)
    {
        var email = Composer().Compose(AccountEmailKind.EmailVerification, Message(language, TimeSpan.FromHours(24)));
        Assert.Equal("Confirma tu correo en MiSaldo", email.Subject);
    }

    [Theory]
    [InlineData(30, "es", "30 minutos")]
    [InlineData(1, "es", "1 minuto")]
    [InlineData(1, "en", "1 minute")]
    [InlineData(0, "en", "1 minute")]
    [InlineData(90, "en", "2 hours")]
    [InlineData(150, "es", "3 horas")]
    public void The_validity_is_written_in_whole_hours_or_minutes(int minutes, string language, string expected)
    {
        var email = Composer().Compose(AccountEmailKind.PasswordReset, Message(language, TimeSpan.FromMinutes(minutes)));
        Assert.Contains($" {expected}.", email.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AccountEmailKind.EmailVerification, "es")]
    [InlineData(AccountEmailKind.EmailVerification, "en")]
    [InlineData(AccountEmailKind.PasswordReset, "es")]
    [InlineData(AccountEmailKind.PasswordReset, "en")]
    public void No_placeholder_is_left_and_no_money_or_password_appears(AccountEmailKind kind, string language)
    {
        var email = Composer().Compose(kind, Message(language, TimeSpan.FromHours(1)));
        foreach (var body in new[] { email.Text, email.Html })
        {
            Assert.DoesNotContain("{{", body, StringComparison.Ordinal);
            Assert.DoesNotContain("COP", body, StringComparison.Ordinal);
            Assert.DoesNotContain("$", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_language_has_every_template_embedded()
    {
        foreach (var language in AccountEmailComposer.Languages)
        {
            Assert.NotEmpty(AccountEmailComposer.Load($"strings.{language}.json"));
            foreach (var kind in Enum.GetValues<AccountEmailKind>())
            {
                var name = AccountEmailComposer.TemplateName(kind);
                Assert.NotEmpty(AccountEmailComposer.Load($"{name}.{language}.txt"));
                Assert.NotEmpty(AccountEmailComposer.Load($"{name}.{language}.html"));
            }
        }
    }

    [Fact]
    public void The_composed_email_never_prints_its_link()
    {
        var email = Composer().Compose(AccountEmailKind.EmailVerification, Message("es", TimeSpan.FromHours(24)));
        Assert.DoesNotContain("tok_abc-123", email.ToString(), StringComparison.Ordinal);
    }
}
