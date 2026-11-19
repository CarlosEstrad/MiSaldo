using System.Text;
using MiSaldo.Api.Common;
using MiSaldo.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Spec 04, task 1: the email and web-client settings are validated at startup.
/// Production refuses to start without SMTP or with a wildcard origin; problems
/// name the setting but never show its value. No Docker needed.
/// </summary>
public class WebClientConfigurationTests
{
    private const string Secret = "s3cr3t-smtp-password-value";

    // A later value for the same key replaces the earlier one, so a base setup can be overridden.
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static (string, string?)[] Mailpit(params (string, string?)[] extra) =>
    [
        ("Smtp:Host", "localhost"),
        ("Smtp:Port", "1025"),
        ("Smtp:Security", "None"),
        ("Smtp:FromAddress", "no-reply@misaldo.test"),
        ("Frontend:BaseUrl", "http://localhost:4200"),
        .. extra,
    ];

    private static string ProblemsOf(IConfiguration configuration, bool isProduction) =>
        Assert.Throws<InvalidOperationException>(() => WebClientConfiguration.Read(configuration, isProduction)).Message;

    // --- SMTP ---

    [Fact]
    public void Without_smtp_development_starts_with_the_in_memory_notifier()
    {
        var settings = WebClientConfiguration.Read(Config(), isProduction: false);
        Assert.False(settings.Smtp.IsConfigured);
        Assert.Null(settings.Frontend.BaseUrl);
        Assert.Empty(settings.Cors.AllowedOrigins);
    }

    [Fact]
    public void Without_smtp_production_does_not_start()
    {
        Assert.Contains("Smtp:Host", ProblemsOf(Config(), isProduction: true), StringComparison.Ordinal);
    }

    [Fact]
    public void Mailpit_settings_are_valid_in_development()
    {
        var settings = WebClientConfiguration.Read(Config(Mailpit()), isProduction: false);
        Assert.True(settings.Smtp.IsConfigured);
        Assert.Equal(SmtpSecurity.None, settings.Smtp.Security);
        Assert.Equal(1025, settings.Smtp.Port);
        Assert.Equal("http://localhost:4200", settings.Frontend.BaseUrl);
    }

    [Fact]
    public void An_unencrypted_smtp_connection_is_refused_in_production()
    {
        var message = ProblemsOf(Config(Mailpit(("Frontend:BaseUrl", "https://app.misaldo.test"))), isProduction: true);
        Assert.Contains("Smtp:Security", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_gmail_style_setup_is_valid_in_production()
    {
        var settings = WebClientConfiguration.Read(Config(
            ("Smtp:Host", "smtp.gmail.com"),
            ("Smtp:Port", "587"),
            ("Smtp:Security", "StartTls"),
            ("Smtp:Username", "misaldo.app@gmail.com"),
            ("Smtp:Password", Secret),
            ("Smtp:FromAddress", "misaldo.app@gmail.com"),
            ("Frontend:BaseUrl", "https://app.misaldo.test/"),
            ("Cors:AllowedOrigins:0", "https://app.misaldo.test")), isProduction: true);

        Assert.Equal(SmtpSecurity.StartTls, settings.Smtp.Security);
        Assert.Equal("https://app.misaldo.test", settings.Frontend.BaseUrl);
    }

    [Theory]
    [InlineData("Smtp:Port", "0", "Smtp:Port")]
    [InlineData("Smtp:Port", "70000", "Smtp:Port")]
    [InlineData("Smtp:FromAddress", "not-an-address", "Smtp:FromAddress")]
    [InlineData("Smtp:FromAddress", "", "Smtp:FromAddress")]
    [InlineData("Smtp:FromName", " ", "Smtp:FromName")]
    [InlineData("Smtp:TimeoutSeconds", "0", "Smtp:TimeoutSeconds")]
    [InlineData("Smtp:TimeoutSeconds", "121", "Smtp:TimeoutSeconds")]
    public void Each_invalid_smtp_setting_is_named(string key, string value, string expected)
    {
        Assert.Contains(expected, ProblemsOf(Config(Mailpit((key, value))), isProduction: false), StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_without_a_user_is_rejected_without_showing_the_password()
    {
        var message = ProblemsOf(Config(Mailpit(("Smtp:Password", Secret))), isProduction: false);
        Assert.Contains("Smtp:Username", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, message, StringComparison.Ordinal);
    }

    [Fact]
    public void All_problems_are_reported_at_once()
    {
        var message = ProblemsOf(Config(Mailpit(("Smtp:Port", "0"), ("Smtp:TimeoutSeconds", "0"), ("Cors:AllowedOrigins:0", "*"))), isProduction: false);
        Assert.Contains("Smtp:Port", message, StringComparison.Ordinal);
        Assert.Contains("Smtp:TimeoutSeconds", message, StringComparison.Ordinal);
        Assert.Contains("Cors:AllowedOrigins:0", message, StringComparison.Ordinal);
    }

    // --- Frontend ---

    [Fact]
    public void Smtp_needs_the_frontend_url_for_the_links()
    {
        var message = ProblemsOf(Config(Mailpit(("Frontend:BaseUrl", null))), isProduction: false);
        Assert.Contains("Frontend:BaseUrl", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("app.misaldo.test")]
    [InlineData("ftp://app.misaldo.test")]
    [InlineData("https://app.misaldo.test/?x=1")]
    [InlineData("https://user:pass@app.misaldo.test")]
    public void A_malformed_frontend_url_is_rejected(string url)
    {
        Assert.Contains("Frontend:BaseUrl", ProblemsOf(Config(Mailpit(("Frontend:BaseUrl", url))), isProduction: false), StringComparison.Ordinal);
    }

    [Fact]
    public void The_frontend_url_must_be_https_in_production()
    {
        var message = ProblemsOf(Config(
            ("Smtp:Host", "smtp.example.com"), ("Smtp:FromAddress", "no-reply@example.com"),
            ("Frontend:BaseUrl", "http://app.example.com")), isProduction: true);
        Assert.Contains("Frontend:BaseUrl", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_frontend_under_a_path_keeps_the_path_without_the_trailing_slash()
    {
        var settings = WebClientConfiguration.Read(Config(Mailpit(("Frontend:BaseUrl", "http://localhost:4200/misaldo/"))), isProduction: false);
        Assert.Equal("http://localhost:4200/misaldo", settings.Frontend.BaseUrl);
    }

    // --- CORS ---

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.misaldo.test")]
    public void A_wildcard_origin_is_rejected(string origin)
    {
        var message = ProblemsOf(Config(("Cors:AllowedOrigins:0", origin)), isProduction: false);
        Assert.Contains("wildcard", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost:4200")]
    [InlineData("https://app.misaldo.test/path")]
    [InlineData("https://app.misaldo.test?x=1")]
    [InlineData("file:///etc/passwd")]
    [InlineData("")]
    public void A_malformed_origin_is_rejected(string origin)
    {
        Assert.Contains("Cors:AllowedOrigins:0", ProblemsOf(Config(("Cors:AllowedOrigins:0", origin)), isProduction: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Origins_are_normalized_and_deduplicated()
    {
        var settings = WebClientConfiguration.Read(Config(
            ("Cors:AllowedOrigins:0", "HTTP://LocalHost:4200/"),
            ("Cors:AllowedOrigins:1", "http://localhost:4200"),
            ("Cors:AllowedOrigins:2", "https://app.misaldo.test:443")), isProduction: false);

        Assert.Equal(["http://localhost:4200", "https://app.misaldo.test"], settings.Cors.AllowedOrigins);
    }

    [Fact]
    public void Origins_must_be_https_in_production()
    {
        var message = ProblemsOf(Config(
            ("Smtp:Host", "smtp.example.com"), ("Smtp:FromAddress", "no-reply@example.com"),
            ("Frontend:BaseUrl", "https://app.example.com"), ("Cors:AllowedOrigins:0", "http://app.example.com")), isProduction: true);
        Assert.Contains("Cors:AllowedOrigins:0", message, StringComparison.Ordinal);
    }

    // --- The real host ---

    [Fact]
    public void The_host_does_not_start_in_production_without_smtp()
    {
        using var factory = ApiFactory.In("Production");
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Smtp:Host", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_does_not_start_in_production_with_a_wildcard_origin()
    {
        using var factory = ApiFactory.In("Production", ProductionSettings(("Cors:AllowedOrigins:0", "*")));
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("wildcard", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_starts_in_production_with_valid_settings()
    {
        using var factory = ApiFactory.In("Production", ProductionSettings());
        using var client = factory.CreateClient();
        Assert.Same(factory.Services.GetRequiredService<SmtpOptions>(), factory.Services.GetRequiredService<WebClientSettings>().Smtp);
        Assert.Equal("https://app.misaldo.test", factory.Services.GetRequiredService<FrontendOptions>().BaseUrl);
    }

    [Fact]
    public void The_host_starts_in_development_without_smtp()
    {
        using var factory = ApiFactory.In("Development");
        using var client = factory.CreateClient();
        Assert.False(factory.Services.GetRequiredService<SmtpOptions>().IsConfigured);
    }

    private static Dictionary<string, string?> ProductionSettings(params (string Key, string? Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "587",
            ["Smtp:Security"] = "StartTls",
            ["Smtp:Username"] = "apikey",
            ["Smtp:Password"] = Secret,
            ["Smtp:FromAddress"] = "no-reply@misaldo.test",
            ["Frontend:BaseUrl"] = "https://app.misaldo.test",
            ["Cors:AllowedOrigins:0"] = "https://app.misaldo.test",
        };
        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static string Flatten(Exception error)
    {
        var text = new StringBuilder();
        for (var current = error; current is not null; current = current.InnerException)
        {
            text.AppendLine(current.Message);
        }

        return text.ToString();
    }
}
