using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Accounts;

namespace MiSaldo.Application.Accounts;

public sealed record VerifyEmailCommand(string Token);

/// <summary>
/// Marks the email as verified when the user presents a valid verification
/// token. The token works once and only until it expires (RF-AUT-03,
/// RNF-SEG-02). Every failure gives the same answer, so a guess reveals nothing.
/// </summary>
public sealed class VerifyEmailHandler
{
    public const string InvalidTokenCode = "verification.invalid_token";

    private readonly IAuthRepository _auth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public VerifyEmailHandler(IAuthRepository auth, IUnitOfWork unitOfWork, ITokenService tokens, IClock clock)
    {
        _auth = auth;
        _unitOfWork = unitOfWork;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(VerifyEmailCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invalid = Result.Failure(Error.Validation(
            InvalidTokenCode, "The verification link is invalid or has expired."));

        if (string.IsNullOrWhiteSpace(command.Token))
        {
            return invalid;
        }

        var now = _clock.UtcNow;
        var token = await _auth.GetOneTimeTokenAsync(
            _tokens.HashToken(command.Token.Trim()), OneTimeTokenPurpose.EmailVerification, cancellationToken);

        if (token is null || !token.IsUsable(now))
        {
            return invalid;
        }

        var credential = await _auth.GetCredentialAsync(token.UserId, cancellationToken);
        if (credential is null)
        {
            return invalid;
        }

        credential.MarkEmailVerified();
        token.Consume(now);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The same token was consumed by a parallel request.
            return invalid;
        }

        return Result.Success();
    }
}
