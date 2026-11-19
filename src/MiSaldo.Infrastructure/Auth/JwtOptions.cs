using System.Text;

namespace MiSaldo.Infrastructure.Auth;

/// <summary>
/// Settings for the JWTs the backend issues. The signing key comes from the
/// environment or a secrets manager, never from the repository (RNF-SEG-08).
/// </summary>
public sealed class JwtOptions
{
    /// <summary>HS256 needs a key of at least 256 bits.</summary>
    public const int MinimumKeyBytes = 32;

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "misaldo";

    public string Audience { get; set; } = "misaldo-api";

    /// <summary>Access token lifetime in minutes; the SRS requires 15 to 60 (RNF-SEG-02).</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    public int RefreshTokenDays { get; set; } = 14;

    public void Validate()
    {
        if (Encoding.UTF8.GetByteCount(SigningKey ?? string.Empty) < MinimumKeyBytes)
        {
            throw new ArgumentException(
                $"The JWT signing key must be at least {MinimumKeyBytes} bytes long.", nameof(SigningKey));
        }

        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new ArgumentException("The JWT issuer is required.", nameof(Issuer));
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new ArgumentException("The JWT audience is required.", nameof(Audience));
        }

        if (AccessTokenMinutes is < 15 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AccessTokenMinutes), AccessTokenMinutes, "The access token must live between 15 and 60 minutes.");
        }

        if (RefreshTokenDays is < 1 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RefreshTokenDays), RefreshTokenDays, "The refresh token must live between 1 and 90 days.");
        }
    }
}

/// <summary>Names of the claims the backend puts in its access tokens.</summary>
public static class JwtClaims
{
    /// <summary>The user id (standard <c>sub</c> claim). The API reads the user only from here (RF-AUT-09).</summary>
    public const string Subject = "sub";

    public const string EmailVerified = "email_verified";
}
