using System.Collections.Concurrent;
using MiSaldo.Application.Abstractions;

namespace MiSaldo.Api.Common;

/// <summary>
/// Stand-in for real email until Amazon SES lands. It keeps the latest
/// verification and reset token per email in memory so the flow can be
/// exercised in development and tests, and logs only that a message was
/// issued: never the token (RNF-SEG-10).
/// </summary>
public sealed class InMemoryAccountNotifier : IAccountNotifier
{
    private readonly ConcurrentDictionary<string, AccountTokenMessage> _verifications = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AccountTokenMessage> _resets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<InMemoryAccountNotifier> _logger;

    public InMemoryAccountNotifier(ILogger<InMemoryAccountNotifier> logger) => _logger = logger;

    public Task SendEmailVerificationAsync(AccountTokenMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _verifications[message.Email] = message;
        _logger.LogInformation("Email verification issued for user {UserId}.", message.UserId);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(AccountTokenMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _resets[message.Email] = message;
        _logger.LogInformation("Password reset issued for user {UserId}.", message.UserId);
        return Task.CompletedTask;
    }

    public AccountTokenMessage? LatestEmailVerification(string email) =>
        _verifications.TryGetValue(email.Trim(), out var message) ? message : null;

    public AccountTokenMessage? LatestPasswordReset(string email) =>
        _resets.TryGetValue(email.Trim(), out var message) ? message : null;
}
