using MiSaldo.Domain.Categories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Verifies user isolation against real PostgreSQL (CP-07). The decisive check:
/// even a raw SQL query that ignores the EF Core query filter cannot see another
/// user's rows, because Row Level Security blocks them at the database. This is
/// the second barrier holding even if the first (EF filter) were bypassed
/// (RNF-SEG-04, RNF-SEG-05).
/// </summary>
[Collection(nameof(PostgresCollection))]
public class IsolationIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public IsolationIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    private static async Task SeedCategoryAsync(PostgresFixture fixture, Guid userId, string name)
    {
        await using var ctx = fixture.CreateContext(userId);
        ctx.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), name));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Ef_query_returns_only_current_users_rows()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedCategoryAsync(_fixture, userA, "A-only");
        await SeedCategoryAsync(_fixture, userB, "B-only");

        await using var ctxA = _fixture.CreateContext(userA);
        var names = await ctxA.ExpenseCategories
            .Select(c => c.Name)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("B-only", names);
        Assert.Contains("A-only", names);
    }

    // CP-07: a raw query that bypasses the EF Core filter still sees no B rows,
    // because RLS blocks them. This is the real isolation guarantee.
    [Fact]
    public async Task Raw_query_cannot_see_other_users_rows_rls_enforced()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedCategoryAsync(_fixture, userA, "raw-A");
        await SeedCategoryAsync(_fixture, userB, "raw-B");

        await using var ctxA = _fixture.CreateContext(userA);

        // Raw SQL over the same connection (session app.user_id = A). No EF filter
        // involved; only RLS decides what rows are visible.
        var connection = (NpgsqlConnection)ctxA.Database.GetDbConnection();
        await ctxA.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM expense_categories;";
            var visibleCount = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

            // A sees only its own categories (at least 1, and none of B's).
            Assert.True(visibleCount >= 1);

            await using var namesCmd = connection.CreateCommand();
            namesCmd.CommandText = "SELECT string_agg(\"Name\", ',') FROM expense_categories;";
            var names = (await namesCmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))?.ToString() ?? string.Empty;
            Assert.DoesNotContain("raw-B", names);
            Assert.Contains("raw-A", names);
        }
        finally
        {
            await ctxA.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task User_b_data_is_invisible_to_user_a_count()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedCategoryAsync(_fixture, userB, "hidden-1");
        await SeedCategoryAsync(_fixture, userB, "hidden-2");

        // User A has no categories; must see zero despite B having rows.
        await using var ctxA = _fixture.CreateContext(userA);
        var count = await ctxA.ExpenseCategories.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, count);

        // And with the raw query too (RLS), still zero.
        await ctxA.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        try
        {
            var connection = ctxA.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM expense_categories;";
            var rawCount = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, rawCount);
        }
        finally
        {
            await ctxA.Database.CloseConnectionAsync();
        }
    }
}
