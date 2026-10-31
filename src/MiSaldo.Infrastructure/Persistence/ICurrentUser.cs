namespace MiSaldo.Infrastructure.Persistence;

/// <summary>
/// Provides the id of the currently authenticated user. The id is resolved from
/// the access token by the API layer, never from query parameters (RF-AUT-09).
/// The persistence layer uses it for the global query filter and for stamping
/// UserId on new entities.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The current user's id, or null when there is no user in context.</summary>
    Guid? UserId { get; }
}
