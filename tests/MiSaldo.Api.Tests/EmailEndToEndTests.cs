using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MiSaldo.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MiSaldo.Api.Tests;

/// <summary>
/// Spec 04, task 8 (🐳): the account emails end to end. The API runs against a
/// real PostgreSQL and sends through a real Mailpit; each test reads the email
/// from Mailpit's HTTP API and follows its link, as a user would (R10.1, R10.2).
/// Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class EmailEndToEndTests : IClassFixture<MailpitFixture>
{
    private const string AuthBase = "/api/v1/auth";
    private const string Frontend = "http://localhost:4200";
    private const string SmtpUser = "misaldo-smtp";
    private const string SmtpPassword = "smtp-password-that-must-never-be-logged";

    private const string VerifySubjectEs = "Confirma tu correo en MiSaldo";
    private const string VerifySubjectEn = "Confirm your email for MiSaldo";
    private const string ResetSubjectEn = "Reset your MiSaldo password";

    private readonly ApiPostgresFixture _database;
    private readonly MailpitFixture _mailpit;

    public EmailEndToEndTests(ApiPostgresFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _mailpit = mailpit;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ApiTestHost HostWithMailpit(Action<IWebHostBuilder>? configure = null) => new(_database, configure: builder =>
    {
        foreach (var (key, value) in _mailpit.Settings(Frontend))
        {
            builder.UseSetting(key, value);
        }

        // Mailpit accepts any credentials, so the authenticated path (and its secret) is exercised too.
        builder.UseSetting("Smtp:Username", SmtpUser);
        builder.UseSetting("Smtp:Password", SmtpPassword);
        configure?.Invoke(builder);
    });

    private static object Registration(string email, string? language) => new
    {
        email,
        password = ApiTestHost.Password,
        fullName = "Ana Perez",
        birthDate = new DateOnly(1990, 5, 1),
        country = "CO",
        acceptsPrivacyPolicy = true,
        acceptsTerms = true,
        language,
    };

    /// <summary>The token in the email's link to <c>{Frontend}/{page}?token=</c>, decoded as the frontend would.</summary>
    private static string TokenFromLink(MailpitMessage message, string page)
    {
        var match = Regex.Match(message.Text, Regex.Escape($"{Frontend}/{page}?token=") + @"(?<token>[^\s""<>]+)");
        Assert.True(match.Success, $"The email has no link to /{page}.");

        // The HTML part carries the same link.
        Assert.Contains($"{Frontend}/{page}?token={match.Groups["token"].Value}", message.Html, StringComparison.Ordinal);
        return Uri.UnescapeDataString(match.Groups["token"].Value);
    }

    private static async Task RegisterAsync(ApiTestHost host, string email, string? language)
    {
        var response = await host.Client.PostAsJsonAsync($"{AuthBase}/register", Registration(email, language), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginAsync(ApiTestHost host, string email, string password = ApiTestHost.Password) =>
        await host.Client.PostAsJsonAsync($"{AuthBase}/login", new { email, password }, Ct);

    // --- R10.1 ---

    [Fact]
    public async Task A_spanish_registration_sends_the_email_and_its_link_verifies_the_account()
    {
        using var host = HostWithMailpit();
        var email = ApiTestHost.UniqueEmail();

        await RegisterAsync(host, email, language: null);

        var message = await _mailpit.Client.WaitForMessageAsync(email, VerifySubjectEs, Ct);
        Assert.Equal(MailpitFixture.FromAddress, message.From.Address);
        Assert.Contains("24 horas", message.Text, StringComparison.Ordinal);
        var token = TokenFromLink(message, "verify-email");

        // Before following the link the session says the email is not verified.
        Assert.False((await ApiTestHost.ReadAsync(await LoginAsync(host, email))).GetProperty("emailVerified").GetBoolean());

        var verify = await host.Client.PostAsJsonAsync($"{AuthBase}/verify-email", new { token }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);

        var login = await ApiTestHost.ReadAsync(await LoginAsync(host, email));
        Assert.True(login.GetProperty("emailVerified").GetBoolean());

        // A verified user reaches the financial routes.
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/categories", Ct)).StatusCode);
    }

    // --- R10.2 ---

    [Fact]
    public async Task An_english_user_gets_english_emails_and_recovers_the_password_through_the_link()
    {
        using var host = HostWithMailpit();
        var email = ApiTestHost.UniqueEmail();
        const string newPassword = "a brand new english password";

        await RegisterAsync(host, email, "en");
        var verification = await _mailpit.Client.WaitForMessageAsync(email, VerifySubjectEn, Ct);
        Assert.Contains("24 hours", verification.Text, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PostAsJsonAsync(
            $"{AuthBase}/verify-email", new { token = TokenFromLink(verification, "verify-email") }, Ct)).StatusCode);

        // An open session, to check that the reset closes it.
        var session = await LoginAsync(host, email);
        var refreshToken = SessionCookies.RefreshTokenOf(session);

        Assert.Equal(HttpStatusCode.Accepted,
            (await host.Client.PostAsJsonAsync($"{AuthBase}/forgot-password", new { email = email.ToUpperInvariant() }, Ct)).StatusCode);

        var reset = await _mailpit.Client.WaitForMessageAsync(email, ResetSubjectEn, Ct);
        Assert.Contains("1 hour", reset.Text, StringComparison.Ordinal);
        Assert.Contains("ignore this email", reset.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiTestHost.Password, reset.Text, StringComparison.Ordinal);

        var response = await host.Client.PostAsJsonAsync(
            $"{AuthBase}/reset-password", new { token = TokenFromLink(reset, "reset-password"), newPassword }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(host, email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(host, email, newPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await host.PostWithRefreshCookieAsync($"{AuthBase}/refresh", refreshToken)).StatusCode);
    }

    [Fact]
    public async Task Password_recovery_for_an_unknown_email_answers_the_same_and_sends_nothing()
    {
        using var host = HostWithMailpit();
        var email = ApiTestHost.UniqueEmail();

        var response = await host.Client.PostAsJsonAsync($"{AuthBase}/forgot-password", new { email }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        Assert.Empty(await _mailpit.Client.SearchToAsync(email, Ct));
    }

    // --- R1.6, R3.1 ---

    [Fact]
    public async Task With_the_smtp_server_down_the_account_is_kept_and_a_resend_works_once_it_is_back()
    {
        var email = ApiTestHost.UniqueEmail();

        // The server is down: the registration still answers 201 and nothing reaches the inbox.
        using (var down = HostWithMailpit(builder =>
        {
            builder.UseSetting("Smtp:Host", "127.0.0.1");
            builder.UseSetting("Smtp:Port", ClosedPort().ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("Smtp:TimeoutSeconds", "2");
        }))
        {
            await RegisterAsync(down, email, language: null);
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(down, email)).StatusCode);
        }

        Assert.Empty(await _mailpit.Client.SearchToAsync(email, Ct));

        // The server is back. A minute later the user asks for a new email.
        var clock = new ShiftableClock();
        clock.Advance(TimeSpan.FromSeconds(61));
        using var back = HostWithMailpit(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock)));

        Assert.Equal(HttpStatusCode.Accepted,
            (await back.Client.PostAsJsonAsync($"{AuthBase}/resend-verification", new { email }, Ct)).StatusCode);

        var message = await _mailpit.Client.WaitForMessageAsync(email, VerifySubjectEs, Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await back.Client.PostAsJsonAsync(
            $"{AuthBase}/verify-email", new { token = TokenFromLink(message, "verify-email") }, Ct)).StatusCode);
        Assert.True((await ApiTestHost.ReadAsync(await LoginAsync(back, email))).GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task A_second_resend_within_a_minute_sends_no_second_email()
    {
        using var host = HostWithMailpit();
        var email = ApiTestHost.UniqueEmail();
        await RegisterAsync(host, email, language: null);
        await _mailpit.Client.WaitForMessageAsync(email, VerifySubjectEs, Ct);

        Assert.Equal(HttpStatusCode.Accepted,
            (await host.Client.PostAsJsonAsync($"{AuthBase}/resend-verification", new { email }, Ct)).StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        Assert.Single(await _mailpit.Client.SearchToAsync(email, Ct));
    }

    // --- R10.6: no secret in any log while real emails go out ---

    [Fact]
    public async Task Sending_real_emails_logs_no_token_link_address_or_smtp_password()
    {
        var sink = new CapturingLoggerProvider();
        using var host = HostWithMailpit(builder =>
        {
            builder.UseSetting("Logging:LogLevel:Default", "Trace");
            builder.UseSetting("Logging:LogLevel:Microsoft", "Trace");
            builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace");
            builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Trace");
            builder.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(sink)));
        });

        var email = ApiTestHost.UniqueEmail();
        const string newPassword = "another long password value";
        var secrets = new List<string> { SmtpPassword, ApiTestHost.Password, newPassword, email };

        await RegisterAsync(host, email, language: null);
        var verification = await _mailpit.Client.WaitForMessageAsync(email, VerifySubjectEs, Ct);
        var verifyToken = TokenFromLink(verification, "verify-email");
        secrets.Add(verifyToken);
        secrets.Add(Uri.EscapeDataString(verifyToken));
        secrets.Add($"{Frontend}/verify-email");
        await host.Client.PostAsJsonAsync($"{AuthBase}/verify-email", new { token = verifyToken }, Ct);

        await host.Client.PostAsJsonAsync($"{AuthBase}/forgot-password", new { email }, Ct);
        var reset = await _mailpit.Client.WaitForMessageAsync(email, "Restablece tu contraseña de MiSaldo", Ct);
        var resetToken = TokenFromLink(reset, "reset-password");
        secrets.Add(resetToken);
        secrets.Add(Uri.EscapeDataString(resetToken));
        secrets.Add($"{Frontend}/reset-password");
        await host.Client.PostAsJsonAsync($"{AuthBase}/reset-password", new { token = resetToken, newPassword }, Ct);

        var entries = sink.Entries.ToList();

        // The sends themselves were logged (by domain only), so the check below covers them.
        Assert.Contains(entries, e => e.Category.EndsWith("SmtpAccountNotifier", StringComparison.Ordinal));
        foreach (var entry in entries)
        {
            foreach (var secret in secrets)
            {
                Assert.False(entry.Text.Contains(secret, StringComparison.Ordinal),
                    $"A log entry leaks a secret: {entry.Category}: {entry.Text.Replace(secret, "<<SECRET>>", StringComparison.Ordinal)}");
            }
        }
    }

    // A port nothing listens on: bind to a free one, then release it.
    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record LogEntry(string Category, string Text);

    /// <summary>Keeps every log entry with its message, structured values, exception and scopes.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var scopes = new System.Text.StringBuilder();
                owner._scopes.ForEachScope((scope, text) => text.Append(scope).Append(';'), scopes);
                var values = state is IEnumerable<KeyValuePair<string, object?>> fields
                    ? string.Join(";", fields.Select(f => $"{f.Key}={f.Value}"))
                    : string.Empty;
                owner.Entries.Enqueue(new LogEntry(category, $"{formatter(state, exception)}|{values}|{exception}|{scopes}"));
            }
        }
    }
}
