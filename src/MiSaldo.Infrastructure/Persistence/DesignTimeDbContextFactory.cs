using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiSaldo.Infrastructure.Persistence;

/// <summary>
/// Creates a <see cref="MiSaldoDbContext"/> at design time so the EF Core tools
/// can generate and apply migrations. It uses a connection string from the
/// <c>MISALDO_DB</c> environment variable, or a local default, and a no-user
/// context since migrations do not run under a specific user. No secrets are
/// hardcoded (RNF-SEG-08).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MiSaldoDbContext>
{
    public MiSaldoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MISALDO_DB")
            ?? "Host=localhost;Port=5432;Database=misaldo;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MiSaldoDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MiSaldoDbContext(options, new DesignTimeCurrentUser());
    }

    private sealed class DesignTimeCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
    }
}
