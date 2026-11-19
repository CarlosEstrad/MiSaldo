using MiSaldo.Application.Abstractions;

namespace MiSaldo.Infrastructure.Persistence;

/// <summary>
/// Provides the id of the currently authenticated user to the persistence layer
/// for the global query filter, the UserId stamping and the Row Level Security
/// session variable. The id is resolved from the access token by the API layer,
/// never from query parameters (RF-AUT-09). It extends the application-level
/// accessor so one object serves both the use cases and the data isolation.
/// </summary>
public interface ICurrentUser : ICurrentUserAccessor
{
}
