using MiSaldo.Domain.Categories;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Expenses;
using MiSaldo.Domain.Periods;
using MiSaldo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Verifies the database-level guarantees against real PostgreSQL: the period
/// non-overlap EXCLUDE constraint (RF-PRD-03), the case-insensitive unique
/// category index (RF-CAT-04), the period end>=start CHECK (RNF-DAT-05), payment
/// idempotency (CP-12), optimistic concurrency (RNF-DAT-04) and multi-device
/// consistency (CP-16). These exercise the raw SQL written in the migration
/// tasks, so this is where that SQL is actually proven correct.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class ConstraintsIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ConstraintsIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Cop(decimal amount) => new(amount, Currency.Cop);

    // RF-PRD-03: two periods of the same user whose ranges overlap are rejected.
    [Fact]
    public async Task Overlapping_periods_for_same_user_are_rejected()
    {
        var userId = Guid.NewGuid();

        await using (var ctx = _fixture.CreateContext(userId))
        {
            ctx.FinancialPeriods.Add(new FinancialPeriod(
                Guid.NewGuid(), PeriodType.Monthly,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), true));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = _fixture.CreateContext(userId))
        {
            ctx.FinancialPeriods.Add(new FinancialPeriod(
                Guid.NewGuid(), PeriodType.Monthly,
                new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15), true)); // overlaps Jan
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task Non_overlapping_periods_for_same_user_are_allowed()
    {
        var userId = Guid.NewGuid();

        await using var ctx = _fixture.CreateContext(userId);
        ctx.FinancialPeriods.Add(new FinancialPeriod(
            Guid.NewGuid(), PeriodType.Monthly,
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), true));
        ctx.FinancialPeriods.Add(new FinancialPeriod(
            Guid.NewGuid(), PeriodType.Monthly,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), true));
        await ctx.SaveChangesAsync(Ct);

        Assert.Equal(2, await ctx.FinancialPeriods.CountAsync(Ct));
    }

    // RF-CAT-04: category name unique per user, case-insensitive.
    [Fact]
    public async Task Duplicate_category_name_case_insensitive_is_rejected()
    {
        var userId = Guid.NewGuid();

        await using (var ctx = _fixture.CreateContext(userId))
        {
            ctx.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "Food"));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = _fixture.CreateContext(userId))
        {
            ctx.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "food")); // differs only in case
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
        }
    }

    [Fact]
    public async Task Same_category_name_for_different_users_is_allowed()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using (var ctx = _fixture.CreateContext(userA))
        {
            ctx.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "Shared"));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = _fixture.CreateContext(userB))
        {
            ctx.ExpenseCategories.Add(new ExpenseCategory(Guid.NewGuid(), "Shared"));
            await ctx.SaveChangesAsync(Ct); // must succeed
            Assert.Equal(1, await ctx.ExpenseCategories.CountAsync(Ct));
        }
    }

    // RNF-DAT-05: a period row with end < start is rejected by the CHECK. The
    // domain prevents building one, so we insert raw SQL to hit the DB guard.
    [Fact]
    public async Task Period_end_before_start_is_rejected_by_check()
    {
        var userId = Guid.NewGuid();
        await using var ctx = _fixture.CreateContext(userId);
        await ctx.Database.OpenConnectionAsync(Ct);
        try
        {
            var connection = (NpgsqlConnection)ctx.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO financial_periods
                    ("Id","Type","StartDate","EndDate","CarryOverEnabled","IsClosed","CreatedAt","UpdatedAt","UserId")
                VALUES (@id,'Monthly','2026-05-31','2026-05-01',true,false, now(), now(), @uid);
                """;
            AddParam(command, "id", Guid.NewGuid());
            AddParam(command, "uid", userId);

            var ex = await Assert.ThrowsAnyAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(Ct));
            Assert.Equal("23514", ex.SqlState); // check_violation
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    // CP-12: the same payment with the same idempotency key persists only once.
    // Payments are inserted directly into the payments set (setting the FK and key
    // as shadow properties) so the unique index is what rejects the duplicate,
    // isolating this test from the parent-expense update path.
    [Fact]
    public async Task Same_idempotency_key_rejects_the_duplicate_payment()
    {
        var userId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        const string key = "idem-key-123";

        await using (var ctx = _fixture.CreateContext(userId))
        {
            ctx.Expenses.Add(new Expense(
                expenseId, "Rent", Guid.NewGuid(), Cop(500_000m),
                new DateOnly(2026, 1, 10), ExpenseType.Fixed));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = _fixture.CreateContext(userId))
        {
            await AddPaymentDirectlyAsync(ctx, expenseId, new DateOnly(2026, 1, 12), key);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = _fixture.CreateContext(userId))
        {
            await AddPaymentDirectlyAsync(ctx, expenseId, new DateOnly(2026, 1, 13), key); // same key
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => ctx.SaveChangesAsync(Ct));
        }

        await using (var verify = _fixture.CreateContext(userId))
        {
            var count = await verify.ExpensePayments.CountAsync(Ct);
            Assert.Equal(1, count);
        }
    }

    private static Task AddPaymentDirectlyAsync(
        MiSaldoDbContext ctx, Guid expenseId, DateOnly date, string idempotencyKey)
    {
        var payment = new ExpensePayment(Guid.NewGuid(), Cop(200_000m), date);
        var entry = ctx.Entry(payment);
        entry.Property("ExpenseId").CurrentValue = expenseId;
        entry.Property("IdempotencyKey").CurrentValue = idempotencyKey;
        entry.State = EntityState.Added;
        return Task.CompletedTask;
    }

    // CP-16: two contexts (two "devices") for the same user see the same data.
    [Fact]
    public async Task Two_contexts_same_user_see_the_same_data()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using (var device1 = _fixture.CreateContext(userId))
        {
            device1.ExpenseCategories.Add(new ExpenseCategory(categoryId, "From device 1"));
            await device1.SaveChangesAsync(Ct);
        }

        await using var device2 = _fixture.CreateContext(userId);
        var seen = await device2.ExpenseCategories.SingleAsync(c => c.Id == categoryId, Ct);
        Assert.Equal("From device 1", seen.Name);
    }

    // RNF-DAT-04: optimistic concurrency via xmin; the stale writer fails.
    [Fact]
    public async Task Concurrent_update_fails_with_optimistic_concurrency()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using (var ctx = _fixture.CreateContext(userId))
        {
            ctx.ExpenseCategories.Add(new ExpenseCategory(categoryId, "Original"));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctxA = _fixture.CreateContext(userId);
        await using var ctxB = _fixture.CreateContext(userId);

        var fromA = await ctxA.ExpenseCategories.SingleAsync(c => c.Id == categoryId, Ct);
        var fromB = await ctxB.ExpenseCategories.SingleAsync(c => c.Id == categoryId, Ct);

        fromA.Rename("Renamed by A");
        await ctxA.SaveChangesAsync(Ct);

        fromB.Rename("Renamed by B"); // B loaded the old row version
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync(Ct));
    }

    private static void AddParam(NpgsqlCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }
}
