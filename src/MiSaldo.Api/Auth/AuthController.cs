using MiSaldo.Api.Common;
using MiSaldo.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace MiSaldo.Api.Auth;

/// <summary>
/// The public authentication endpoints. They only translate HTTP to the use
/// cases and back: the rules live in <c>MiSaldo.Application</c>. Responses that
/// carry tokens are never cached.
/// </summary>
[AllowAnonymous]
[Route($"{ApiRoutes.V1}/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Creates an account and sends the email verification token.</summary>
    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(
        RegisterRequest request, [FromServices] RegisterUserHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RegisterUserCommand(
                request.Email,
                request.Password,
                request.FullName,
                request.BirthDate!.Value,
                request.Country,
                request.Gender,
                request.AcceptsPrivacyPolicy,
                request.AcceptsTerms,
                request.Language),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, new RegisterResponse(result.Value.UserId))
            : Failure(result.Error!);
    }

    /// <summary>Marks the email as verified with the single-use token.</summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        VerifyEmailRequest request, [FromServices] VerifyEmailHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new VerifyEmailCommand(request.Token), cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    /// <summary>Signs in and returns an access token and a rotating refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginRequest request, [FromServices] LoginHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    /// <summary>Exchanges a refresh token for a new pair; the old refresh token stops working.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokens>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        RefreshTokenRequest request, [FromServices] RefreshSessionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RefreshSessionCommand(request.RefreshToken), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    /// <summary>Revokes the refresh token. Always succeeds, even for an unknown token.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        RefreshTokenRequest request, [FromServices] LogoutHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LogoutCommand(request.RefreshToken), cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    /// <summary>Starts a password recovery. The answer is the same whether or not the email exists.</summary>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request, [FromServices] ForgotPasswordHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ForgotPasswordCommand(request.Email), cancellationToken);
        return result.IsSuccess ? Accepted() : Failure(result.Error!);
    }

    /// <summary>Sets a new password with the recovery token and closes every active session.</summary>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request, [FromServices] ResetPasswordHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }
}
