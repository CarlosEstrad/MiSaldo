using MiSaldo.Domain.Categories;
using MiSaldo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Verifies the global per-user query filter and UserId stamping using the EF
/// Core in-memory provider (no database, no Docker). The filter is the first
/// isolation barrier (RNF-SEG-04); RLS is the second and is tested against real
/// PostgreSQL later.
/// </summary>
public class GlobalUserFilterTests
{
    private static MiSaldoDbContext ContextFor(ICurrentUser user, string dbName)
    {
        var options = new DbContextOptionsBuilder<MiSaldoDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new MiSaldoDbContext(options, user);
    }

    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    [Fact]
    public void Query_only_returns_current_user_entities()
    {
        var db = $"filter-{Guid.NewGuid()}";

        // Seed a category for A and one for B, each through their own context.
        using (var ctxA = ContextFor(new TestCurrentUser(UserA), db))
        {
            ctxA.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "Food A"));
            ctxA.SaveChanges();
        }

        using (var ctxB = ContextFor(new TestCurrentUser(UserB), db))
        {
            ctxB.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "Food B"));
            ctxB.SaveChanges();
        }

        // A only sees its own.
        using var queryA = ContextFor(new TestCurrentUser(UserA), db);
        var namesForA = queryA.ExpenseCategories.Select(c => c.Name).ToList();
        Assert.Equal(["Food A"], namesForA);
    }

    [Fact]
    public void Create_stamps_the_current_user_id()
    {
        var db = $"stamp-{Guid.NewGuid()}";
        var category = new ExpenseCategory(Guid.NewGuid(), "Transport");

        using (var ctx = ContextFor(new TestCurrentUser(UserA), db))
        {
            ctx.ExpenseCategories.Add(category);
            ctx.SaveChanges();

            var userId = ctx.Entry(category).Property("UserId").CurrentValue;
            Assert.Equal(UserA, userId);
        }
    }

    [Fact]
    public void Saving_without_a_current_user_fails_safe()
    {
        var db = $"nouser-{Guid.NewGuid()}";
        using var ctx = ContextFor(new TestCurrentUser(userId: null), db);
        ctx.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "Health"));

        Assert.Throws<InvalidOperationException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void Ignoring_the_filter_reveals_other_users_rows()
    {
        // Confirms the filter is what scopes results (and that IgnoreQueryFilters
        // bypasses it) -- a sanity check that the filter is actually applied.
        var db = $"ignore-{Guid.NewGuid()}";

        using (var ctxA = ContextFor(new TestCurrentUser(UserA), db))
        {
            ctxA.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "A1"));
            ctxA.SaveChanges();
        }

        using (var ctxB = ContextFor(new TestCurrentUser(UserB), db))
        {
            ctxB.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "B1"));
            ctxB.SaveChanges();
        }

        using var query = ContextFor(new TestCurrentUser(UserA), db);
        Assert.Single(query.ExpenseCategories.ToList());              // filtered
        Assert.Equal(2, query.ExpenseCategories.IgnoreQueryFilters().Count()); // unfiltered
    }
}
