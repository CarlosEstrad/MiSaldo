using MailKit.Net.Smtp;
using MailKit.Security;
using MiSaldo.Application.Abstractions;
using Microsoft.Extensions.Logging;
using MimeKit;
namespace MiSaldo.Infrastructure.Email;

/// <summary>
/// Sends the account emails over SMTP with MailKit (R1, D-13). The same code
/// serves Mailpit, a dedicated Gmail account or a transactional provider; only
/// <see cref="SmtpOptions"/> change.
/// <para>
/// A failed or slow delivery never fails the request: the account and the token
/// are already stored, the user can ask for a new email, and the answer to the
/// caller must not change (R3). The failure is logged with the kind of error and
/// the recipient's domain only: never the token, the link, the password or the
/// full address (R1.6, RNF-SEG-10). MailKit's exception messages can quote the
/// server's reply, so they are not logged either.
/// </para>
/// </summary>
public sealed class SmtpAccountNotifier : IAccountNotifier
{
    private readonly SmtpOptions _options;
    private readonly AccountEmailComposer _composer;
    private readonly ILogger<SmtpAccountNotifier> _logger;

    public SmtpAccountNotifier(SmtpOptions options, AccountEmailComposer composer, ILogger<SmtpAccountNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsConfigured)
        {
            throw new ArgumentException("The SMTP notifier needs a server (Smtp:Host).", nameof(options));
        }

        _options = options;
        _composer = composer;
        _logger = logger;
    }

    public Task SendEmailVerificationAsync(AccountTokenMessage message, CancellationToken cancellationToken = default) =>
        SendAsync(AccountEmailKind.EmailVerification, message, cancellationToken);

    public Task SendPasswordResetAsync(AccountTokenMessage message, CancellationToken cancellationToken = default) =>
        SendAsync(AccountEmailKind.PasswordReset, message, cancellationToken);

    private async Task SendAsync(AccountEmailKind kind, AccountTokenMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var domain = RecipientDomain(message.Email);
        var email = _composer.Compose(kind, message);
        using var mime = new MimeMessage
        {
            Subject = email.Subject,
            Body = new BodyBuilder { TextBody = email.Text, HtmlBody = email.Html }.ToMessageBody(),
        };
        // Host and sender are checked at startup (SmtpOptions.Validate), so they are set here.
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress!));
        mime.To.Add(MailboxAddress.Parse(message.Email));

        // The whole exchange (connect, authenticate, send) shares one deadline (R3.3).
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            using var client = new SmtpClient { Timeout = _options.TimeoutSeconds * 1000 };
            await client.ConnectAsync(_options.Host!, _options.Port, SocketOptions(_options.Security), deadline.Token);
            if (!string.IsNullOrEmpty(_options.Username))
            {
                // Username and password are validated to come together.
                await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, deadline.Token);
            }

            await client.SendAsync(new SmtpSendRequest(mime), deadline.Token);
            await client.DisconnectAsync(quit: true, deadline.Token);
            _logger.LogInformation("Account email {EmailKind} sent to a recipient at {RecipientDomain}.", kind, domain);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away; nothing to report.
            throw;
        }
        // Two clocks guard the exchange (the shared deadline and MailKit's own
        // timeout); whichever fires first, it is the same event and is logged the same way.
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException
            || ex.InnerException is TimeoutException || deadline.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Account email {EmailKind} to a recipient at {RecipientDomain} was not sent: the SMTP server did not answer within {TimeoutSeconds} s.",
                kind, domain, _options.TimeoutSeconds);
        }
        catch (Exception ex)
        {
            var status = ex is SmtpCommandException command ? (int)command.StatusCode : (int?)null;
            _logger.LogWarning(
                "Account email {EmailKind} to a recipient at {RecipientDomain} was not sent ({ErrorType}, SMTP status {SmtpStatus}).",
                kind, domain, ex.GetType().Name, status);
        }
    }

    private static SecureSocketOptions SocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => throw new ArgumentOutOfRangeException(nameof(security), security, "Unknown SMTP security mode."),
    };

    // The domain is enough to spot a provider-wide problem without logging who the user is.
    private static string RecipientDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 && at < email.Length - 1 ? email[(at + 1)..].ToLowerInvariant() : "unknown";
    }
}
