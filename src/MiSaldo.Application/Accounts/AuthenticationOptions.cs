namespace MiSaldo.Application.Accounts;

/// <summary>Settings for login lockout and password reset (RF-AUT-06, RF-AUT-07).</summary>
public sealed record AuthenticationOptions
{
    /// <summary>Consecutive failed logins that lock the account.</summary>
    public int MaxFailedLoginAttempts { get; init; } = 5;

    /// <summary>How long a locked account stays locked.</summary>
    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan PasswordResetTokenLifetime { get; init; } = TimeSpan.FromHours(1);
}
