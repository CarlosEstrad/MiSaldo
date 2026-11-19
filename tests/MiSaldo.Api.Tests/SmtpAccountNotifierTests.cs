using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MiSaldo.Api.Common;
using MiSaldo.Application.Abstractions;
using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Spec 04, task 4 (🐳): the SMTP notifier delivers the account emails to a real
/// Mailpit, and a down or silent server never fails the caller nor leaks a
/// secret into the logs. Needs Docker running.
/// </summary>
public sealed class SmtpAccountNotifierTests : IClassFixture<MailpitFixture>
{
    private const string Frontend = "http://localhost:4200";
    private const string SmtpPassword = "smtp-password-that-must-not-leak";

    private readonly MailpitFixture _mailpit;

    public SmtpAccountNotifierTests(MailpitFixture mailpit) => _mailpit = mailpit;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static AccountTokenMessage Message(string email, string language, TimeSpan validFor) =>
        new(Guid.NewGuid(), email, $"tok-{Guid.NewGuid():N}", language, DateTimeOffset.UtcNow + validFor);

    private static SmtpAccountNotifier Notifier(SmtpOptions options, ListLogger<SmtpAccountNotifier>? logger = null) =>
        new(options, new AccountEmailComposer(Frontend, new SystemClock()), logger ?? new ListLogger<SmtpAccountNotifier>());

    [Fact]
    public async Task A_verification_email_reaches_the_inbox_with_its_sender_subject_and_link()
    {
        var email = UniqueEmail();
        var message = Message(email, "es", TimeSpan.FromHours(24));
        var logger = new ListLogger<SmtpAccountNotifier>();

        await Notifier(_mailpit.Options(), logger).SendEmailVerificationAsync(message, Ct);

        var received = await _mailpit.Client.WaitForMessageToAsync(email, Ct);
        Assert.Equal(MailpitFixture.FromAddress, received.From.Address);
        Assert.Equal("MiSaldo", received.From.Name);
        Assert.Equal(email, Assert.Single(received.To).Address);
        Assert.Equal("Confirma tu correo en MiSaldo", received.Subject);

        var link = $"{Frontend}/verify-email?token={message.Token}";
        Assert.Contains(link, received.Text, StringComparison.Ordinal);
        Assert.Contains($"href=\"{link}\"", received.Html, StringComparison.Ordinal);
        Assert.Contains("24 horas", received.Text, StringComparison.Ordinal);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("example.com", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reset_email_is_sent_with_smtp_credentials_in_the_users_language()
    {
        var email = UniqueEmail();
        var message = Message(email, "en", TimeSpan.FromHours(1));

        await Notifier(_mailpit.Options(username: "misaldo", password: SmtpPassword)).SendPasswordResetAsync(message, Ct);

        var received = await _mailpit.Client.WaitForMessageToAsync(email, Ct);
        Assert.Equal("Reset your MiSaldo password", received.Subject);
        Assert.Contains($"{Frontend}/reset-password?token={message.Token}", received.Text, StringComparison.Ordinal);
        Assert.Contains("ignore this email", received.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_that_is_down_does_not_fail_the_caller_and_leaks_nothing()
    {
        var logger = new ListLogger<SmtpAccountNotifier>();
        var options = _mailpit.Options(username: "misaldo", password: SmtpPassword) with { Host = "127.0.0.1", Port = ClosedPort() };
        var message = Message("ana.perez@example.org", "es", TimeSpan.FromHours(24));

        await Notifier(options, logger).SendEmailVerificationAsync(message, Ct);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("example.org", entry.Message, StringComparison.Ordinal);
        AssertNoSecret(entry.Message, message);
    }

    [Fact]
    public async Task A_silent_server_is_given_up_after_the_timeout()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var accepting = silent.AcceptTcpClientAsync(Ct).AsTask();

        var logger = new ListLogger<SmtpAccountNotifier>();
        var options = _mailpit.Options(timeoutSeconds: 1) with { Host = "127.0.0.1", Port = ((IPEndPoint)silent.LocalEndpoint).Port };
        var message = Message(UniqueEmail(), "es", TimeSpan.FromHours(24));

        var watch = Stopwatch.StartNew();
        await Notifier(options, logger).SendEmailVerificationAsync(message, Ct);
        watch.Stop();

        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(8));
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("did not answer within 1 s", entry.Message, StringComparison.Ordinal);
        AssertNoSecret(entry.Message, message);
        (await accepting).Dispose();
    }

    [Fact]
    public async Task A_cancelled_request_is_not_reported_as_a_delivery_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var logger = new ListLogger<SmtpAccountNotifier>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Notifier(_mailpit.Options(), logger).SendEmailVerificationAsync(Message(UniqueEmail(), "es", TimeSpan.FromHours(1)), cancelled.Token));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void With_smtp_settings_the_host_uses_the_smtp_notifier()
    {
        using var factory = ApiFactory.In("Development", new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "1025",
            ["Smtp:Security"] = "None",
            ["Smtp:FromAddress"] = MailpitFixture.FromAddress,
            ["Frontend:BaseUrl"] = Frontend,
        });

        Assert.IsType<SmtpAccountNotifier>(factory.Services.GetRequiredService<IAccountNotifier>());
        Assert.Null(factory.Services.GetService<InMemoryAccountNotifier>());
    }

    [Fact]
    public void Without_smtp_settings_development_keeps_the_in_memory_notifier()
    {
        using var factory = ApiFactory.In("Development");
        Assert.IsType<InMemoryAccountNotifier>(factory.Services.GetRequiredService<IAccountNotifier>());
    }

    private static void AssertNoSecret(string logText, AccountTokenMessage message)
    {
        Assert.DoesNotContain(message.Token, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(message.Email, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(Frontend, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(SmtpPassword, logText, StringComparison.Ordinal);
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
}

/// <summary>Keeps every log entry of one category with its level and rendered message.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? string.Empty : "|" + exception)));
        }
    }
}
