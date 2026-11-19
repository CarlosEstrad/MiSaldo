using MiSaldo.Application.Abstractions;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>Captures the messages a use case would send, so tests can read the tokens.</summary>
public sealed class RecordingAccountNotifier : IAccountNotifier
{
    public List<AccountTokenMessage> EmailVerifications { get; } = [];

    public List<AccountTokenMessage> PasswordResets { get; } = [];

    public Task SendEmailVerificationAsync(AccountTokenMessage message, CancellationToken cancellationToken = default)
    {
        EmailVerifications.Add(message);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(AccountTokenMessage message, CancellationToken cancellationToken = default)
    {
        PasswordResets.Add(message);
        return Task.CompletedTask;
    }
}
