using MiSaldo.Domain.Categories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Smoke test for the integration fixture: a real PostgreSQL container with the
/// migrations applied, the app role and RLS active. Confirms a round-trip
/// insert and read works end to end (task 7).
/// </summary>
[Collection(nameof(PostgresCollection))]
public class SmokeIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public SmokeIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Insert_and_read_a_category_round_trips()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var ct = TestContext.Current.CancellationToken;

        await using (var write = _fixture.CreateContext(userId))
        {
            write.ExpenseCategories.Add(new ExpenseCategory(categoryId, "Groceries"));
            await write.SaveChangesAsync(ct);
        }

        await using (var read = _fixture.CreateContext(userId))
        {
            var found = await read.ExpenseCategories.SingleAsync(ct);
            Assert.Equal(categoryId, found.Id);
            Assert.Equal("Groceries", found.Name);
        }
    }
}
