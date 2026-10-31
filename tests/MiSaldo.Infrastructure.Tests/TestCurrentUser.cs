using MiSaldo.Infrastructure.Persistence;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>A controllable <see cref="ICurrentUser"/> for tests.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public TestCurrentUser(Guid? userId = null) => UserId = userId;

    public Guid? UserId { get; set; }
}
