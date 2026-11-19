using MiSaldo.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
namespace MiSaldo.Api.Auth;

/// <summary>Authorization policies beyond "has a valid token".</summary>
public static class AuthPolicies
{
    /// <summary>
    /// Financial routes need a verified email (RF-AUT-03). An unverified user
    /// is authenticated, so the answer is 403, not 401.
    /// </summary>
    public const string VerifiedEmail = "VerifiedEmail";

    public static void Add(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(VerifiedEmail, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(JwtClaims.EmailVerified, "true"));
    }
}
