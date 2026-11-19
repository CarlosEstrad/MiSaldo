using MiSaldo.Application.Common;
namespace MiSaldo.Application.Abstractions;

/// <summary>A single-use token to deliver to the account owner (verification or reset).</summary>
public sealed record AccountTokenMessage(Guid UserId, string Email, string Token)
{
    public override string ToString() => Redaction.Of(nameof(AccountTokenMessage));
}

/// <summary>
/// Delivers account messages such as the email verification and password reset
/// tokens. Real email (Amazon SES) arrives in a later phase; until then a
/// development implementation exposes the token through a controlled channel so
/// the flow can be tested. Implementations must never log the token
/// (RNF-SEG-10).
/// </summary>
public interface IAccountNotifier
{
    Task SendEmailVerificationAsync(AccountTokenMessage message, CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(AccountTokenMessage message, CancellationToken cancellationToken = default);
}
