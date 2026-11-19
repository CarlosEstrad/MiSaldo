using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;

namespace MiSaldo.Application.Accounts;

public sealed record ForgotPasswordCommand(string Email)
{
    public override string ToString() => Redaction.Of(nameof(ForgotPasswordCommand));
}

/// <summary>
/// Starts a password reset. The answer is always success, whether or not the
/// email has an account, so the endpoint cannot be used to find accounts
/// (RF-AUT-06). Only an existing account gets a reset token.
/// </summary>
public sealed class ForgotPasswordHandler
{
    private readonly IUserRepository _users;
    private readonly IAuthRepository _auth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly IAccountNotifier _notifier;
    private readonly AuthenticationOptions _options;

    public ForgotPasswordHandler(
        IUserRepository users,
        IAuthRepository auth,
        IUnitOfWork unitOfWork,
        ITokenService tokens,
        IClock clock,
        IAccountNotifier notifier,
        AuthenticationOptions options)
    {
        _users = users;
        _auth = auth;
        _unitOfWork = unitOfWork;
        _tokens = tokens;
        _clock = clock;
        _notifier = notifier;
        _options = options;
    }

    public async Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken = default)
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

        var now = _clock.UtcNow;
        var expiresAt = now + _options.PasswordResetTokenLifetime;
        var resetToken = _tokens.CreateOpaqueToken();
        _auth.Add(new OneTimeToken(
            Guid.NewGuid(),
            profile.UserId,
            OneTimeTokenPurpose.PasswordReset,
            resetToken.Hash,
            now,
            expiresAt));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        // The email goes out in the user's language; settings always exist for a registered user.
        var settings = await _users.GetSettingsAsync(profile.UserId, cancellationToken);
        var language = RegistrationValidator.NormalizeLanguage(settings?.Language) ?? RegistrationOptions.FallbackLanguage;
        await _notifier.SendPasswordResetAsync(
            new AccountTokenMessage(profile.UserId, email, resetToken.Value, language, expiresAt), cancellationToken);

        return Result.Success();
    }
}

public sealed record ResetPasswordCommand(string Token, string NewPassword)
{
    public override string ToString() => Redaction.Of(nameof(ResetPasswordCommand));
}

/// <summary>
/// Sets a new password with a valid reset token. The token works once and only
/// until it expires, the lockout is cleared, and every open session is closed
/// so a stolen refresh token stops working (RF-AUT-06, RNF-SEG-02).
/// </summary>
public sealed class ResetPasswordHandler
{
    public const string InvalidTokenCode = "reset.invalid_token";

    private readonly IAuthRepository _auth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public ResetPasswordHandler(
        IAuthRepository auth,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokens,
        IClock clock)
    {
        _auth = auth;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var policy = PasswordPolicy.Validate(command.NewPassword);
        if (policy.IsFailure)
        {
            return policy;
        }

        var invalid = Result.Failure(Error.Validation(
            InvalidTokenCode, "The reset link is invalid or has expired."));

        if (string.IsNullOrWhiteSpace(command.Token))
        {
            return invalid;
        }

        var now = _clock.UtcNow;
        var token = await _auth.GetOneTimeTokenAsync(
            _tokens.HashToken(command.Token.Trim()), OneTimeTokenPurpose.PasswordReset, cancellationToken);

        if (token is null || !token.IsUsable(now))
        {
            return invalid;
        }

        var credential = await _auth.GetCredentialAsync(token.UserId, cancellationToken);
        if (credential is null)
        {
            return invalid;
        }

        credential.ChangePasswordHash(_passwordHasher.Hash(command.NewPassword));
        token.Consume(now);

        foreach (var session in await _auth.ListActiveRefreshTokensAsync(token.UserId, now, cancellationToken))
        {
            session.Revoke(now);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The same token was used by a parallel request.
            return invalid;
        }

        return Result.Success();
    }
}
