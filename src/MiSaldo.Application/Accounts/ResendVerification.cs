using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;

namespace MiSaldo.Application.Accounts;

public sealed record ResendVerificationCommand(string Email)
{
    public override string ToString() => Redaction.Of(nameof(ResendVerificationCommand));
}

/// <summary>
/// Sends a new email verification link (RF-AUT-03). Only an existing, not yet
/// verified account gets one: its earlier verification tokens stop working and a
/// new one is issued, unless the last one is less than a minute old. The answer
/// is always success, so the endpoint reveals nothing about the account
/// (RNF-SEG-05).
/// </summary>
public sealed class ResendVerificationHandler
{
    private readonly IUserRepository _users;
    private readonly IAuthRepository _auth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly IAccountNotifier _notifier;
    private readonly RegistrationOptions _options;

    public ResendVerificationHandler(
        IUserRepository users,
        IAuthRepository auth,
        IUnitOfWork unitOfWork,
        ITokenService tokens,
        IClock clock,
        IAccountNotifier notifier,
        RegistrationOptions options)
    {
        _users = users;
        _auth = auth;
        _unitOfWork = unitOfWork;
        _tokens = tokens;
        _clock = clock;
        _notifier = notifier;
        _options = options;
    }

    public async Task<Result> HandleAsync(ResendVerificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return Result.Success();
        }

        var email = RegistrationValidator.NormalizeEmail(command.Email);
        var profile = await _users.GetProfileByEmailAsync(email, cancellationToken);
        if (profile is null)
        {
            return Result.Success();
        }

        var credential = await _auth.GetCredentialAsync(profile.UserId, cancellationToken);
        if (credential is null || credential.EmailVerified)
        {
            return Result.Success();
        }

        var now = _clock.UtcNow;
        var usable = await _auth.ListUsableOneTimeTokensAsync(
            profile.UserId, OneTimeTokenPurpose.EmailVerification, now, cancellationToken);

        // One email a minute at most, so the endpoint cannot be used to flood an inbox (R4.4).
        if (usable.Any(t => now - t.CreatedAt < _options.VerificationResendCooldown))
        {
            return Result.Success();
        }

        // Only the newest link works after a resend (RNF-SEG-02).
        foreach (var previous in usable)
        {
            previous.Consume(now);
        }

        var expiresAt = now + _options.EmailVerificationTokenLifetime;
        var verificationToken = _tokens.CreateOpaqueToken();
        _auth.Add(new OneTimeToken(
            Guid.NewGuid(),
            profile.UserId,
            OneTimeTokenPurpose.EmailVerification,
            verificationToken.Hash,
            now,
            expiresAt));

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // A parallel resend replaced the same tokens first; that one sends the email.
            return Result.Success();
        }

        var settings = await _users.GetSettingsAsync(profile.UserId, cancellationToken);
        var language = RegistrationValidator.NormalizeLanguage(settings?.Language) ?? RegistrationOptions.FallbackLanguage;
        await _notifier.SendEmailVerificationAsync(
            new AccountTokenMessage(profile.UserId, email, verificationToken.Value, language, expiresAt), cancellationToken);

        return Result.Success();
    }
}
