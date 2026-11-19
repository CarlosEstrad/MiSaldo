using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MiSaldo.Application.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MiSaldo.Infrastructure.Auth;

/// <summary>
/// Issues the backend's own tokens: short-lived signed JWT access tokens
/// (stateless, never stored) and random opaque tokens that are stored only by
/// their SHA-256 hash (RNF-SEG-02). Because it sits behind <see cref="ITokenService"/>,
/// the identity provider can change later without touching the use cases.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private const int OpaqueTokenBytes = 32;

    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(JwtOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();

        _options = options;
        _clock = clock;
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public AccessToken CreateAccessToken(Guid userId, bool emailVerified)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new[]
        {
            new Claim(JwtClaims.Subject, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(
                JwtClaims.EmailVerified,
                emailVerified.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                ClaimValueTypes.Boolean),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _signingCredentials,
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }

    public OpaqueToken CreateOpaqueToken()
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(OpaqueTokenBytes));
        return new OpaqueToken(value, HashToken(value));
    }

    // The tokens are 256-bit random values, so a plain SHA-256 is enough: there
    // is nothing to brute-force, and lookups by hash stay fast and indexable.
    public string HashToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
