using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Categories;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Expenses;
using MiSaldo.Domain.Incomes;
using MiSaldo.Domain.Periods;
using MiSaldo.Infrastructure.Persistence.Repositories;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Verifies the repositories and the unit of work against real PostgreSQL, with
/// the app role (RLS active) and the interceptors wired in, exactly as the API
/// will use them.
/// </summary>
[Collection(nameof(PostgresCollection))]
public class RepositoryIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public RepositoryIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Money Cop(decimal amount) => new(amount, Currency.Cop);

    // --- Categories ---

    [Fact]
    public async Task Category_name_check_ignores_case_and_can_exclude_itself()
    {
        var userId = Guid.NewGuid();
        var category = new ExpenseCategory(Guid.NewGuid(), "Food");

        await using (var db = _fixture.CreateContext(userId))
        {
            new CategoryRepository(db).Add(category);
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new CategoryRepository(db);
            Assert.True(await repo.NameExistsAsync("food", null, Ct));
            Assert.True(await repo.NameExistsAsync("  FOOD ", null, Ct));
            Assert.False(await repo.NameExistsAsync("FOOD", category.Id, Ct)); // renaming itself
            Assert.False(await repo.NameExistsAsync("Transport", null, Ct));
        }
    }

    [Fact]
    public async Task Category_lists_only_the_current_users_categories_in_order()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userA))
        {
            var repo = new CategoryRepository(db);
            repo.Add(new ExpenseCategory(Guid.NewGuid(), "Second", sortOrder: 2));
            repo.Add(new ExpenseCategory(Guid.NewGuid(), "First", sortOrder: 1));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userB))
        {
            new CategoryRepository(db).Add(new ExpenseCategory(Guid.NewGuid(), "Other user"));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userA))
        {
            var list = await new CategoryRepository(db).ListAsync(Ct);
            Assert.Equal(["First", "Second"], list.Select(c => c.Name).ToArray());
        }
    }

    [Fact]
    public async Task Category_has_movements_only_when_an_expense_uses_it()
    {
        var userId = Guid.NewGuid();
        var usedId = Guid.NewGuid();
        var unusedId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userId))
        {
            new CategoryRepository(db).Add(new ExpenseCategory(usedId, "Used"));
            new CategoryRepository(db).Add(new ExpenseCategory(unusedId, "Unused"));
            new ExpenseRepository(db).Add(new Expense(
                Guid.NewGuid(), "Rent", usedId, Cop(500_000m), new DateOnly(2026, 1, 5), ExpenseType.Fixed));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new CategoryRepository(db);
            Assert.True(await repo.HasMovementsAsync(usedId, Ct));
            Assert.False(await repo.HasMovementsAsync(unusedId, Ct));
        }
    }

    // --- Periods ---

    [Fact]
    public async Task Period_overlap_detection_is_inclusive()
    {
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userId))
        {
            new PeriodRepository(db).Add(new FinancialPeriod(
                Guid.NewGuid(), PeriodType.Monthly, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), true));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new PeriodRepository(db);
            Assert.True(await repo.OverlapsAsync(new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15), Ct));
            Assert.True(await repo.OverlapsAsync(new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 31), Ct)); // touching end
            Assert.False(await repo.OverlapsAsync(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), Ct));
        }
    }

    [Fact]
    public async Task Overdue_periods_are_the_ended_ones_that_are_still_open()
    {
        var userId = Guid.NewGuid();
        var periodId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userId))
        {
            new PeriodRepository(db).Add(new FinancialPeriod(
                periodId, PeriodType.Monthly, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), true));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new PeriodRepository(db);
            Assert.Empty(await repo.ListOverdueAsync(new DateOnly(2026, 1, 20), Ct)); // still running
            var overdue = await repo.ListOverdueAsync(new DateOnly(2026, 2, 5), Ct);
            Assert.Equal(periodId, Assert.Single(overdue).Id);

            overdue[0].Close();
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            Assert.Empty(await new PeriodRepository(db).ListOverdueAsync(new DateOnly(2026, 2, 5), Ct));
        }
    }

    // --- Incomes ---

    [Fact]
    public async Task Incomes_for_a_range_include_planned_by_date_and_received_by_reception()
    {
        var userId = Guid.NewGuid();
        var plannedInRange = Guid.NewGuid();
        var receivedInRange = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new IncomeRepository(db);
            repo.Add(new IncomeTransaction(
                plannedInRange, "Bonus", Cop(100_000m), new DateOnly(2026, 1, 10), "Work", IncomeStatus.Planned));
            repo.Add(new IncomeTransaction(
                receivedInRange, "Salary", Cop(2_000_000m), new DateOnly(2025, 12, 28), "Work",
                IncomeStatus.Received, new DateOnly(2026, 1, 2)));
            repo.Add(new IncomeTransaction(
                Guid.NewGuid(), "Later", Cop(50_000m), new DateOnly(2026, 3, 1), "Work", IncomeStatus.Planned));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            var list = await new IncomeRepository(db).ListForRangeAsync(
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), Ct);

            Assert.Equal(2, list.Count);
            Assert.Contains(list, i => i.Id == plannedInRange);
            Assert.Contains(list, i => i.Id == receivedInRange);
        }
    }

    // --- Expenses ---

    [Fact]
    public async Task Expense_is_loaded_with_its_payments()
    {
        var userId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userId))
        {
            var expense = new Expense(
                expenseId, "Rent", Guid.NewGuid(), Cop(500_000m), new DateOnly(2026, 1, 5), ExpenseType.Fixed);
            expense.AddPayment(new ExpensePayment(Guid.NewGuid(), Cop(200_000m), new DateOnly(2026, 1, 6)));
            expense.AddPayment(new ExpensePayment(Guid.NewGuid(), Cop(100_000m), new DateOnly(2026, 1, 20)));
            new ExpenseRepository(db).Add(expense);
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            var loaded = await new ExpenseRepository(db).GetByIdAsync(expenseId, Ct);

            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.Payments.Count);
            Assert.Equal(300_000m, loaded.TotalPaid().Amount);
            Assert.Equal(200_000m, loaded.Outstanding().Amount);
        }
    }

    [Fact]
    public async Task Expenses_for_a_range_include_those_dated_or_paid_in_it()
    {
        var userId = Guid.NewGuid();
        var datedInRange = Guid.NewGuid();
        var paidInRange = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new ExpenseRepository(db);
            repo.Add(new Expense(
                datedInRange, "Dated", Guid.NewGuid(), Cop(100_000m), new DateOnly(2026, 1, 10), ExpenseType.Variable));

            var paid = new Expense(
                paidInRange, "Paid later", Guid.NewGuid(), Cop(100_000m), new DateOnly(2025, 12, 20), ExpenseType.Variable);
            paid.AddPayment(new ExpensePayment(Guid.NewGuid(), Cop(100_000m), new DateOnly(2026, 1, 5)));
            repo.Add(paid);

            repo.Add(new Expense(
                Guid.NewGuid(), "Outside", Guid.NewGuid(), Cop(100_000m), new DateOnly(2026, 3, 1), ExpenseType.Variable));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            var list = await new ExpenseRepository(db).ListForRangeAsync(
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), Ct);

            Assert.Equal(2, list.Count);
            Assert.Contains(list, e => e.Id == datedInRange);
            Assert.Contains(list, e => e.Id == paidInRange && e.Payments.Count == 1);
        }
    }

    [Fact]
    public async Task Another_users_expense_is_not_found_by_id()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(owner))
        {
            new ExpenseRepository(db).Add(new Expense(
                expenseId, "Private", Guid.NewGuid(), Cop(10_000m), new DateOnly(2026, 1, 5), ExpenseType.Fixed));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(other))
        {
            Assert.Null(await new ExpenseRepository(db).GetByIdAsync(expenseId, Ct));
        }
    }

    // --- Users ---

    [Fact]
    public async Task User_account_round_trips_and_email_lookup_ignores_case()
    {
        var userId = Guid.NewGuid();
        var today = new DateOnly(2026, 1, 15);

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new UserRepository(db);
            repo.Add(User.Register(userId, $"ext-{userId}", new DateOnly(1990, 1, 1), today));
            repo.Add(new UserProfile(Guid.NewGuid(), userId, "Ana Perez", "Ana@Example.com", "CO"));
            repo.Add(new UserSettings(
                Guid.NewGuid(), userId, Currency.Cop, "es", ThemePreference.System,
                PeriodType.Monthly, new PeriodSettings()));
            repo.Add(new ConsentRecord(Guid.NewGuid(), userId, "1.0", "1.0", DateTimeOffset.UtcNow));
            await new UnitOfWork(db).SaveChangesAsync(Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new UserRepository(db);

            Assert.True(await repo.EmailExistsAsync("ana@example.com", Ct));
            Assert.False(await repo.EmailExistsAsync("nobody@example.com", Ct));

            var profile = await repo.GetProfileByEmailAsync("ANA@EXAMPLE.COM", Ct);
            Assert.NotNull(profile);
            Assert.Equal(userId, profile.UserId);

            var settings = await repo.GetSettingsAsync(userId, Ct);
            Assert.NotNull(settings);
            Assert.Equal("COP", settings.PrimaryCurrency.Code);
            Assert.Equal("es", settings.Language);

            var user = await repo.GetByIdAsync(userId, Ct);
            Assert.NotNull(user);
            var createdAt = (DateTimeOffset)db.Entry(user).Property("CreatedAt").CurrentValue!;
            Assert.True(createdAt > DateTimeOffset.UtcNow.AddMinutes(-5), "CreatedAt must be stamped on insert.");
        }
    }

    // --- Unit of work ---

    [Fact]
    public async Task Transaction_commits_everything_when_the_action_succeeds()
    {
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new CategoryRepository(db);
            var uow = new UnitOfWork(db);
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                repo.Add(new ExpenseCategory(Guid.NewGuid(), "One"));
                await uow.SaveChangesAsync(ct);
                repo.Add(new ExpenseCategory(Guid.NewGuid(), "Two"));
                await uow.SaveChangesAsync(ct);
            }, Ct);
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            Assert.Equal(2, (await new CategoryRepository(db).ListAsync(Ct)).Count);
        }
    }

    [Fact]
    public async Task Transaction_rolls_back_everything_when_the_action_throws()
    {
        var userId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext(userId))
        {
            var repo = new CategoryRepository(db);
            var uow = new UnitOfWork(db);

            await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInTransactionAsync(async ct =>
            {
                repo.Add(new ExpenseCategory(Guid.NewGuid(), "Doomed"));
                await uow.SaveChangesAsync(ct);
                throw new InvalidOperationException("boom");
            }, Ct));
        }

        await using (var db = _fixture.CreateContext(userId))
        {
            Assert.Empty(await new CategoryRepository(db).ListAsync(Ct));
        }
    }
}
