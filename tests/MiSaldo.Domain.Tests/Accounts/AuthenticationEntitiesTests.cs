using MiSaldo.Domain.Accounts;
using Xunit;

namespace MiSaldo.Domain.Tests.Accounts;

public class UserCredentialTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

    private static UserCredential NewCredential() => new(Guid.NewGuid(), Guid.NewGuid(), "hash");

    [Fact]
    public void New_credential_is_unverified_and_unlocked()
    {
        var credential = NewCredential();

        Assert.False(credential.EmailVerified);
        Assert.Equal(0, credential.FailedLoginAttempts);
        Assert.False(credential.IsLocked(Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_hash_is_rejected(string hash)
    {
        Assert.Throws<ArgumentException>(() => new UserCredential(Guid.NewGuid(), Guid.NewGuid(), hash));
    }

    [Fact]
    public void Verifying_the_email_is_recorded()
    {
        var credential = NewCredential();
        credential.MarkEmailVerified();
        Assert.True(credential.EmailVerified);
    }

    // RF-AUT-07: lock after the configured number of failed attempts.
    [Fact]
    public void Account_locks_when_failed_attempts_reach_the_limit()
    {
        var credential = NewCredential();

        Assert.False(credential.RegisterFailedLogin(Now, 3, LockFor));
        Assert.False(credential.RegisterFailedLogin(Now, 3, LockFor));
        Assert.False(credential.IsLocked(Now));

        Assert.True(credential.RegisterFailedLogin(Now, 3, LockFor)); // third attempt locks

        Assert.True(credential.IsLocked(Now));
        Assert.Equal(Now + LockFor, credential.LockedUntil);
        Assert.Equal(0, credential.FailedLoginAttempts);
    }

    [Fact]
    public void Lock_expires_after_its_duration()
    {
        var credential = NewCredential();
        credential.RegisterFailedLogin(Now, 1, LockFor);

        Assert.True(credential.IsLocked(Now.AddMinutes(14)));
        Assert.False(credential.IsLocked(Now.AddMinutes(15)));
    }

    [Fact]
    public void An_expired_lock_gives_a_fresh_set_of_attempts()
    {
        var credential = NewCredential();
        credential.RegisterFailedLogin(Now, 2, LockFor);
        credential.RegisterFailedLogin(Now, 2, LockFor); // locked

        var later = Now.AddMinutes(30);
        Assert.False(credential.RegisterFailedLogin(later, 2, LockFor)); // 1 of 2, not locked again

        Assert.False(credential.IsLocked(later));
        Assert.Equal(1, credential.FailedLoginAttempts);
    }

    [Fact]
    public void Successful_login_resets_attempts()
    {
        var credential = NewCredential();
        credential.RegisterFailedLogin(Now, 5, LockFor);
        credential.RegisterFailedLogin(Now, 5, LockFor);

        credential.RegisterSuccessfulLogin();

        Assert.Equal(0, credential.FailedLoginAttempts);
        Assert.Null(credential.LockedUntil);
    }

    [Fact]
    public void Changing_the_password_clears_the_lockout()
    {
        var credential = NewCredential();
        credential.RegisterFailedLogin(Now, 1, LockFor);
        Assert.True(credential.IsLocked(Now));

        credential.ChangePasswordHash("new-hash");

        Assert.Equal("new-hash", credential.PasswordHash);
        Assert.False(credential.IsLocked(Now));
        Assert.Equal(0, credential.FailedLoginAttempts);
    }

    [Fact]
    public void Invalid_lock_settings_are_rejected()
    {
        var credential = NewCredential();
        Assert.Throws<ArgumentOutOfRangeException>(() => credential.RegisterFailedLogin(Now, 0, LockFor));
        Assert.Throws<ArgumentOutOfRangeException>(() => credential.RegisterFailedLogin(Now, 3, TimeSpan.Zero));
    }
}

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static RefreshToken NewToken() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "hash-1", Now, Now.AddDays(14));

    [Fact]
    public void New_token_is_active_until_it_expires()
    {
        var token = NewToken();

        Assert.True(token.IsActive(Now));
        Assert.True(token.IsActive(Now.AddDays(14).AddSeconds(-1)));
        Assert.False(token.IsActive(Now.AddDays(14)));
    }

    // RNF-SEG-02: rotation consumes the old token and records its replacement.
    [Fact]
    public void Rotating_consumes_the_token_and_records_the_replacement()
    {
        var token = NewToken();

        token.Rotate(Now.AddMinutes(1), "hash-2");

        Assert.False(token.IsActive(Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(1), token.ConsumedAt);
        Assert.Equal("hash-2", token.ReplacedByTokenHash);
    }

    [Fact]
    public void A_consumed_token_cannot_be_rotated_again()
    {
        var token = NewToken();
        token.Rotate(Now, "hash-2");

        Assert.Throws<InvalidOperationException>(() => token.Rotate(Now.AddMinutes(1), "hash-3"));
    }

    [Fact]
    public void An_expired_token_cannot_be_rotated()
    {
        var token = NewToken();
        Assert.Throws<InvalidOperationException>(() => token.Rotate(Now.AddDays(15), "hash-2"));
    }

    // RF-AUT-05: logout invalidates the refresh token.
    [Fact]
    public void Revoking_invalidates_the_token_without_a_replacement()
    {
        var token = NewToken();

        token.Revoke(Now.AddMinutes(5));

        Assert.False(token.IsActive(Now.AddMinutes(6)));
        Assert.Null(token.ReplacedByTokenHash);
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_moment()
    {
        var token = NewToken();
        token.Revoke(Now.AddMinutes(5));
        token.Revoke(Now.AddMinutes(9));
        Assert.Equal(Now.AddMinutes(5), token.ConsumedAt);
    }

    [Fact]
    public void Invalid_construction_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), " ", Now, Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() => new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "h", Now, Now));
    }
}

public class OneTimeTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static OneTimeToken NewToken(OneTimeTokenPurpose purpose = OneTimeTokenPurpose.EmailVerification) =>
        new(Guid.NewGuid(), Guid.NewGuid(), purpose, "hash", Now, Now.AddHours(24));

    [Fact]
    public void New_token_is_usable_until_it_expires()
    {
        var token = NewToken();

        Assert.True(token.IsUsable(Now));
        Assert.False(token.IsUsable(Now.AddHours(24)));
    }

    [Fact]
    public void Consuming_makes_it_unusable()
    {
        var token = NewToken(OneTimeTokenPurpose.PasswordReset);

        token.Consume(Now.AddMinutes(1));

        Assert.Equal(OneTimeTokenPurpose.PasswordReset, token.Purpose);
        Assert.False(token.IsUsable(Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(1), token.ConsumedAt);
    }

    [Fact]
    public void A_consumed_token_cannot_be_consumed_again()
    {
        var token = NewToken();
        token.Consume(Now);
        Assert.Throws<InvalidOperationException>(() => token.Consume(Now.AddMinutes(1)));
    }

    [Fact]
    public void An_expired_token_cannot_be_consumed()
    {
        var token = NewToken();
        Assert.Throws<InvalidOperationException>(() => token.Consume(Now.AddHours(25)));
    }

    [Fact]
    public void Invalid_construction_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new OneTimeToken(Guid.NewGuid(), Guid.NewGuid(), OneTimeTokenPurpose.PasswordReset, "", Now, Now.AddHours(1)));
        Assert.Throws<ArgumentException>(() =>
            new OneTimeToken(Guid.NewGuid(), Guid.NewGuid(), OneTimeTokenPurpose.PasswordReset, "h", Now, Now.AddHours(-1)));
    }
}
