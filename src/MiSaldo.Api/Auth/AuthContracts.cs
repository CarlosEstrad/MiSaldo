using System.ComponentModel.DataAnnotations;
using MiSaldo.Application.Common;
namespace MiSaldo.Api.Auth;

/// <summary>Body of <c>POST /auth/register</c> (RF-AUT-01).</summary>
public sealed record RegisterRequest(
    [Required] string Email,
    [Required] string Password,
    [Required] string FullName,
    [Required] DateOnly? BirthDate,
    [Required] string Country,
    string? Gender,
    bool AcceptsPrivacyPolicy,
    bool AcceptsTerms,
    string? Language = null)
{
    public override string ToString() => Redaction.Of(nameof(RegisterRequest));
}

/// <summary>Answer of a successful registration.</summary>
public sealed record RegisterResponse(Guid UserId);

/// <summary>Body of <c>POST /auth/verify-email</c>.</summary>
public sealed record VerifyEmailRequest([Required] string Token)
{
    public override string ToString() => Redaction.Of(nameof(VerifyEmailRequest));
}

/// <summary>Body of <c>POST /auth/login</c>.</summary>
public sealed record LoginRequest([Required] string Email, [Required] string Password)
{
    public override string ToString() => Redaction.Of(nameof(LoginRequest));
}

/// <summary>Body of <c>POST /auth/refresh</c> and <c>POST /auth/logout</c>.</summary>
public sealed record RefreshTokenRequest([Required] string RefreshToken)
{
    public override string ToString() => Redaction.Of(nameof(RefreshTokenRequest));
}

/// <summary>Body of <c>POST /auth/forgot-password</c>.</summary>
public sealed record ForgotPasswordRequest([Required] string Email)
{
    public override string ToString() => Redaction.Of(nameof(ForgotPasswordRequest));
}

/// <summary>Body of <c>POST /auth/reset-password</c>.</summary>
public sealed record ResetPasswordRequest([Required] string Token, [Required] string NewPassword)
{
    public override string ToString() => Redaction.Of(nameof(ResetPasswordRequest));
}
