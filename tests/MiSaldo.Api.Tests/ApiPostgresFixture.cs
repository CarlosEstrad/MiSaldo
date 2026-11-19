using MiSaldo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// A real PostgreSQL in a container for the API integration tests. Migrations
/// are applied as the owner; the API then connects as a non-superuser role that
/// does NOT bypass RLS (RNF-SEG-04), exactly as in production. Needs Docker.
/// </summary>
public sealed class ApiPostgresFixture : IAsyncLifetime
{
    private const string AppRole = "misaldo_app";
    private const string AppPassword = "app_pwd";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("misaldo")
        .WithUsername("owner")
        .WithPassword("owner_pwd")
        .Build();

    /// <summary>Connection string for the app role the API uses (RLS applies).</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var ownerConnectionString = _container.GetConnectionString();

        var ownerOptions = new DbContextOptionsBuilder<MiSaldoDbContext>().UseNpgsql(ownerConnectionString).Options;
        await using (var migrationContext = new MiSaldoDbContext(ownerOptions, new NoUser()))
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using (var admin = new NpgsqlConnection(ownerConnectionString))
        {
            await admin.OpenAsync();
            foreach (var sql in new[]
            {
                $"DROP ROLE IF EXISTS {AppRole};",
                $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS;",
                $"GRANT USAGE ON SCHEMA public TO {AppRole};",
                $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole};",
                $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole};",
            })
            {
                await using var command = admin.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }
        }

        AppConnectionString = new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = AppRole,
            Password = AppPassword,
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private sealed class NoUser : ICurrentUser
    {
        public Guid? UserId => null;
    }
}

/// <summary>Shares one container across the API integration test classes.</summary>
[CollectionDefinition(nameof(ApiDatabaseCollection))]
public sealed class ApiDatabaseCollection : ICollectionFixture<ApiPostgresFixture>;
