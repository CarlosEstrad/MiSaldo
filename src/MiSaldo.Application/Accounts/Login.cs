using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;

namespace MiSaldo.Application.Accounts;

public sealed record LoginCommand(string Email, string Password)
{
    public override string ToString() => Redaction.Of(nameof(LoginCommand));
}

/// <summary>
/// Checks the credentials and opens a session (RF-AUT-04). Failed attempts are
/// counted and lock the account for a while (RF-AUT-07). Every failure, a
/// locked account included, gets the same answer, so the endpoint never
/// confirms whether an email has an account (RNF-SEG-05). A login with an
/// unverified email succeeds, but its token says so and the financial routes
/// refuse it (RF-AUT-03).
/// </summary>
public sealed class LoginHandler
{
    public const string InvalidCredentialsCode = "auth.invalid_credentials";

    // Verified against when the email is unknown, so that path costs about as
    // much as a real check and response times do not reveal which emails exist.
    private static string? s_dummyHash;

    private readonly IUserRepository _users;
    private readonly IAuthRepository _auth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly AuthenticationOptions _options;

    public LoginHandler(
        IUserRepository users,
        IAuthRepository auth,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokens,
        IClock clock,
        AuthenticationOptions options)
    {
        _users = users;
        _auth = auth;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _clock = clock;
        _options = options;
    }

    public async Task<Result<AuthTokens>> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invalid = Result.Failure<AuthTokens>(Error.Unauthorized(
            InvalidCredentialsCode,
            "The email or password is incorrect, or the account is temporarily locked. Try again later."));

        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrEmpty(command.Password))
        {
            return invalid;
        }

        var now = _clock.UtcNow;
        var profile = await _users.GetProfileByEmailAsync(
            RegistrationValidator.NormalizeEmail(command.Email), cancellationToken);
        var credential = profile is null
            ? null
            : await _auth.GetCredentialAsync(profile.UserId, cancellationToken);

        if (credential is null)
        {
            s_dummyHash ??= _passwordHasher.Hash("not-a-real-password-for-timing");
            _ = _passwordHasher.Verify(s_dummyHash, command.Password);
            return invalid;
        }

        // While locked, the password is not even checked, so the lock cannot be
        // used to keep guessing.
        if (credential.IsLocked(now))
        {
            return invalid;
        }

        if (!_passwordHasher.Verify(credential.PasswordHash, command.Password))
        {
            credential.RegisterFailedLogin(now, _options.MaxFailedLoginAttempts, _options.LockoutDuration);
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException)
            {
                // A parallel attempt updated the counter first; the answer is the same.
            }

            return invalid;
        }

        credential.RegisterSuccessfulLogin();
        var session = SessionTokens.Issue(
            _auth, _tokens, credential.UserId, credential.EmailVerified, now, _tokens.CreateOpaqueToken());

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // A parallel request changed the credential; nothing was saved, so
            // no session exists. The client can simply try again.
            return invalid;
        }

        return Result.Success(session);
    }
}
