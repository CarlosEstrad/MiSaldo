using MiSaldo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Spins up a real PostgreSQL in a container for the suite, applies the
/// migrations as the owner, then creates a non-superuser application role that
/// does NOT bypass RLS (RNF-SEG-04). Tests query through the app role so Row
/// Level Security actually applies. The container is disposed at the end.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string AppRole = "misaldo_app";
    private const string AppPassword = "app_pwd";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("misaldo")
        .WithUsername("owner")
        .WithPassword("owner_pwd")
        .Build();

    /// <summary>Connection string for the non-superuser app role (RLS applies).</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <summary>Connection string for the owner role (migrations, setup).</summary>
    public string OwnerConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        OwnerConnectionString = _container.GetConnectionString();

        // Apply migrations as the owner.
        var ownerOptions = new DbContextOptionsBuilder<MiSaldoDbContext>()
            .UseNpgsql(OwnerConnectionString)
            .Options;
        await using (var migrationContext = new MiSaldoDbContext(ownerOptions, new TestCurrentUser()))
        {
            await migrationContext.Database.MigrateAsync();
        }

        // Create a non-superuser app role and grant it access. It must not have
        // BYPASSRLS, so the RLS policies constrain it fully.
        await using (var admin = new NpgsqlConnection(OwnerConnectionString))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin,
                $"DROP ROLE IF EXISTS {AppRole};",
                $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS;",
                $"GRANT USAGE ON SCHEMA public TO {AppRole};",
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole};",
                $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole};");
        }

        var appBuilder = new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = AppRole,
            Password = AppPassword,
        };
        AppConnectionString = appBuilder.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>Creates a context as the app role (RLS active) for the given user.</summary>
    public MiSaldoDbContext CreateContext(Guid? userId)
    {
        var currentUser = new TestCurrentUser(userId);
        var options = new DbContextOptionsBuilder<MiSaldoDbContext>();
        MiSaldoDbContextConfigurator.Configure(options, AppConnectionString, currentUser);
        return new MiSaldoDbContext(options.Options, currentUser);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, params string[] statements)
    {
        foreach (var sql in statements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>Collection so the container is shared across integration test classes.</summary>
[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
