namespace MiSaldo.Application.Abstractions;

/// <summary>
/// Exposes the id of the authenticated user to the use cases. The id comes from
/// the access token claim, never from request parameters (RF-AUT-09). The
/// infrastructure <c>ICurrentUser</c> extends this so one object serves both
/// the use cases and the per-user data isolation.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>The current user's id, or null when there is no authenticated user.</summary>
    Guid? UserId { get; }
}
