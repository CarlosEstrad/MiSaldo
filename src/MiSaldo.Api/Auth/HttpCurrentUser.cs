using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Persistence;

namespace MiSaldo.Api.Auth;

/// <summary>
/// The current user of an HTTP request, read only from the <c>sub</c> claim of
/// a validated access token, never from the route, query or body (RF-AUT-09).
/// Without an authenticated user it is null, and the persistence layer then
/// sees no per-user data at all.
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return Guid.TryParse(user.FindFirst(JwtClaims.Subject)?.Value, out var id) && id != Guid.Empty
                ? id
                : null;
        }
    }
}
