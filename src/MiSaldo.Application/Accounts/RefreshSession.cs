using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;

namespace MiSaldo.Application.Accounts;

public sealed record RefreshSessionCommand(string RefreshToken)
{
    public override string ToString() => Redaction.Of(nameof(RefreshSessionCommand));
}

/// <summary>
/// Exchanges a valid refresh token for a new access token and a new refresh
/// token. The old refresh token is consumed (rotation, RNF-SEG-02). If a token
/// that was already rotated shows up again, someone else may hold a copy, so
/// every open session of that user is revoked.
/// </summary>
public sealed class RefreshSessionHandler
{
    public const string InvalidRefreshTokenCode = "auth.invalid_refresh_token";

    private readonly IAuthRepository _auth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public RefreshSessionHandler(IAuthRepository auth, IUnitOfWork unitOfWork, ITokenService tokens, IClock clock)
    {
        _auth = auth;
        _unitOfWork = unitOfWork;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<Result<AuthTokens>> HandleAsync(
        RefreshSessionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var invalid = Result.Failure<AuthTokens>(Error.Unauthorized(
            InvalidRefreshTokenCode, "The session has expired. Sign in again."));

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return invalid;
        }

        var now = _clock.UtcNow;
        var stored = await _auth.GetRefreshTokenAsync(
            _tokens.HashToken(command.RefreshToken.Trim()), cancellationToken);

        if (stored is null)
        {
            return invalid;
        }

        if (!stored.IsActive(now))
        {
            if (stored.ReplacedByTokenHash is not null)
            {
                await RevokeAllSessionsAsync(stored.UserId, now, cancellationToken);
            }

            return invalid;
        }

        var credential = await _auth.GetCredentialAsync(stored.UserId, cancellationToken);
        if (credential is null)
        {
            return invalid;
        }

        var next = _tokens.CreateOpaqueToken();
        stored.Rotate(now, next.Hash);
        var session = SessionTokens.Issue(_auth, _tokens, stored.UserId, credential.EmailVerified, now, next);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // The same refresh token was rotated by a parallel request; only one wins.
            return invalid;
        }

        return Result.Success(session);
    }

    private async Task RevokeAllSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var token in await _auth.ListActiveRefreshTokensAsync(userId, now, cancellationToken))
        {
            token.Revoke(now);
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Another request touched the same tokens; they end up revoked or rotated either way.
        }
    }
}

public sealed record LogoutCommand(string RefreshToken)
{
    public override string ToString() => Redaction.Of(nameof(LogoutCommand));
}

/// <summary>
/// Closes a session by revoking its refresh token (RF-AUT-05). Logging out is
/// always reported as successful, even for an unknown or already revoked token.
/// </summary>
public sealed class LogoutHandler
{
    private readonly IAuthRepository _auth;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public LogoutHandler(IAuthRepository auth, IUnitOfWork unitOfWork, ITokenService tokens, IClock clock)
    {
        _auth = auth;
        _unitOfWork = unitOfWork;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(LogoutCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Success();
        }

        var stored = await _auth.GetRefreshTokenAsync(
            _tokens.HashToken(command.RefreshToken.Trim()), cancellationToken);

        if (stored is null || stored.ConsumedAt is not null)
        {
            return Result.Success();
        }

        stored.Revoke(_clock.UtcNow);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // A parallel request already consumed the token.
        }

        return Result.Success();
    }
}
