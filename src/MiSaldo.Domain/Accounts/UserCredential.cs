namespace MiSaldo.Domain.Accounts;

/// <summary>
/// The login credentials of a user: the password hash, whether the email is
/// verified, and the failed-attempt counter that drives the temporary lockout
/// (RF-AUT-03, RF-AUT-07). Only a hash is ever stored (RNF-SEG-10).
/// </summary>
public sealed class UserCredential
{
    public UserCredential(Guid id, Guid userId, string passwordHash)
    {
        RequireHash(passwordHash);

        Id = id;
        UserId = userId;
        PasswordHash = passwordHash;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public string PasswordHash { get; private set; }

    public bool EmailVerified { get; private set; }

    /// <summary>Consecutive failed logins since the last success or lockout.</summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>Until when login is blocked, or null when the account is not locked.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public void MarkEmailVerified() => EmailVerified = true;

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && now < until;

    /// <summary>
    /// Records a failed login. When the attempts reach <paramref name="maxAttempts"/>
    /// the account is locked for <paramref name="lockDuration"/> and the counter
    /// restarts. A lock that already expired is cleared first, so the user gets a
    /// fresh set of attempts. Returns true when this attempt caused the lockout.
    /// </summary>
    public bool RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockDuration)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");
        }

        if (lockDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lockDuration), lockDuration, "The lock duration must be positive.");
        }

        if (LockedUntil is { } until && now >= until)
        {
            LockedUntil = null;
            FailedLoginAttempts = 0;
        }

        FailedLoginAttempts++;
        if (FailedLoginAttempts < maxAttempts)
        {
            return false;
        }

        LockedUntil = now + lockDuration;
        FailedLoginAttempts = 0;
        return true;
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }

    /// <summary>Replaces the hash (password reset) and clears any lockout.</summary>
    public void ChangePasswordHash(string newPasswordHash)
    {
        RequireHash(newPasswordHash);

        PasswordHash = newPasswordHash;
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }

    private static void RequireHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        }
    }
}
