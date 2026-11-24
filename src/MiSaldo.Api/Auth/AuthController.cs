using MiSaldo.Api.Common;
using MiSaldo.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace MiSaldo.Api.Auth;

/// <summary>
/// The public authentication endpoints. They only translate HTTP to the use
/// cases and back: the rules live in <c>MiSaldo.Application</c>. The refresh
/// token lives in <see cref="RefreshCookie"/>, never in a body. Responses that
/// carry tokens or touch the cookie are never cached.
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

    /// <summary>
    /// Sends a new verification email when the account exists and is not verified;
    /// earlier links stop working. The answer is always 202, whatever the case.
    /// </summary>
    [HttpPost("resend-verification")]
    [EndpointSummary("Resend the email verification link")]
    [EndpointDescription(
        "Sends a new verification email when the account exists and is not verified yet; earlier links stop working. " +
        "Nothing is sent if the last verification email is less than a minute old. The answer is always 202 with no body, " +
        "whether or not the account exists or is verified, so it reveals nothing. Under the strict /auth rate limit.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerification(
        ResendVerificationRequest request, [FromServices] ResendVerificationHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ResendVerificationCommand(request.Email), cancellationToken);
        return result.IsSuccess ? Accepted() : Failure(result.Error!);
    }

    /// <summary>
    /// Signs in. The body carries the access token; the rotating refresh token is
    /// set in the <c>__Secure-misaldo-refresh</c> cookie (HttpOnly, Secure,
    /// SameSite=Strict, Path=/api/v1/auth).
    /// </summary>
    [HttpPost("login")]
    [EndpointSummary("Sign in")]
    [EndpointDescription(
        "The body returns the access token (keep it in memory only), its expiry, the session expiry and whether the email is verified. " +
        "The refresh token is NOT in the body: it is set in the " + RefreshCookie.Name + " cookie " +
        "(HttpOnly, Secure, SameSite=Strict, Path=" + RefreshCookie.Path + ", Max-Age = session lifetime). " +
        "Browsers must send requests with credentials so the cookie is stored.")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginRequest request, [FromServices] LoginHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password), cancellationToken);
        return result.IsSuccess ? Session(result.Value) : Failure(result.Error!);
    }

    /// <summary>
    /// Renews the session with the refresh cookie; no body. The cookie is
    /// replaced and the old token stops working. A missing, invalid, reused or
    /// expired cookie gets 401 and is deleted.
    /// </summary>
    [HttpPost("refresh")]
    [EndpointSummary("Renew the session with the refresh cookie")]
    [EndpointDescription(
        "No body: the refresh token is read from the " + RefreshCookie.Name + " cookie. On success the cookie is replaced " +
        "(the old token stops working) and the body returns a new access token. A missing, invalid, reused or expired cookie " +
        "gets 401 auth.invalid_refresh_token and the cookie is deleted; reusing a rotated token closes every session of the user. " +
        "A request whose Origin is neither an allowed origin nor the API's own gets 403 auth.origin_not_allowed and changes nothing.")]
    [SameOriginGuard]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromServices] RefreshSessionHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new RefreshSessionCommand(RefreshCookie.Read(Request) ?? string.Empty), cancellationToken);
        if (result.IsFailure)
        {
            RefreshCookie.Delete(Response);
            return Failure(result.Error!);
        }

        return Session(result.Value);
    }

    /// <summary>Revokes the session of the refresh cookie and deletes it; no body. Always 204.</summary>
    [HttpPost("logout")]
    [EndpointSummary("Sign out")]
    [EndpointDescription(
        "No body: revokes the session of the " + RefreshCookie.Name + " cookie and deletes the cookie. Always 204, even without a cookie. " +
        "A request whose Origin is neither an allowed origin nor the API's own gets 403 auth.origin_not_allowed and changes nothing.")]
    [SameOriginGuard]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromServices] LogoutHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new LogoutCommand(RefreshCookie.Read(Request) ?? string.Empty), cancellationToken);
        RefreshCookie.Delete(Response);
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
    [EndpointSummary("Set a new password with the recovery token")]
    [EndpointDescription(
        "The token comes from the link in the recovery email and works once. On success every session of the user is closed " +
        "and the " + RefreshCookie.Name + " cookie of this browser is deleted.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request, [FromServices] ResetPasswordHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken);
        if (result.IsFailure)
        {
            return Failure(result.Error!);
        }

        // Every session was revoked; this browser's cookie goes too (R5.5).
        RefreshCookie.Delete(Response);
        return NoContent();
    }

    private OkObjectResult Session(AuthTokens tokens)
    {
        RefreshCookie.Write(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
        return Ok(SessionResponse.From(tokens));
    }
}
