using System.ComponentModel.DataAnnotations;
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
    bool AcceptsTerms);

/// <summary>Answer of a successful registration.</summary>
public sealed record RegisterResponse(Guid UserId);

/// <summary>Body of <c>POST /auth/verify-email</c>.</summary>
public sealed record VerifyEmailRequest([Required] string Token);

/// <summary>Body of <c>POST /auth/login</c>.</summary>
public sealed record LoginRequest([Required] string Email, [Required] string Password);

/// <summary>Body of <c>POST /auth/refresh</c> and <c>POST /auth/logout</c>.</summary>
public sealed record RefreshTokenRequest([Required] string RefreshToken);

/// <summary>Body of <c>POST /auth/forgot-password</c>.</summary>
public sealed record ForgotPasswordRequest([Required] string Email);

/// <summary>Body of <c>POST /auth/reset-password</c>.</summary>
public sealed record ResetPasswordRequest([Required] string Token, [Required] string NewPassword);
