using MiSaldo.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence;

/// <summary>
/// Configures a <see cref="MiSaldoDbContext"/> options builder for PostgreSQL
/// with the RLS interceptor wired in. Both the API and the integration tests use
/// this so the second isolation barrier is always active (RNF-SEG-04).
/// </summary>
public static class MiSaldoDbContextConfigurator
{
    public static DbContextOptionsBuilder<MiSaldoDbContext> Configure(
        DbContextOptionsBuilder<MiSaldoDbContext> builder,
        string connectionString,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(currentUser);

        builder
            .UseNpgsql(connectionString)
            .AddInterceptors(
                new RlsConnectionInterceptor(currentUser),
                new AuditingInterceptor(currentUser));

        return builder;
    }
}
