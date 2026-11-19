using System.Text;
using MiSaldo.Infrastructure.Auth;
using Microsoft.IdentityModel.Tokens;

namespace MiSaldo.Api.Auth;

/// <summary>
/// How the API checks the access tokens the backend itself issued: same key,
/// issuer and audience as <see cref="JwtTokenService"/>, HS256 only, and an
/// expiry that is always enforced.
/// </summary>
public static class JwtValidation
{
    /// <summary>Tolerance for small clock differences between hosts.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static TokenValidationParameters CreateParameters(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
            NameClaimType = JwtClaims.Subject,
        };
    }
}
