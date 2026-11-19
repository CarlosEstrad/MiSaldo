using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Expenses;
using MiSaldo.Domain.Incomes;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Task 10 (🐳): periods over HTTP against a real PostgreSQL. Dates are
/// relative to today because the access tokens are checked against the real
/// clock. Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class PeriodEndpointsTests : IDisposable
{
    private const string Periods = "/api/v1/periods";

    private readonly ApiTestHost _host;

    public PeriodEndpointsTests(ApiPostgresFixture database) => _host = new ApiTestHost(database);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // "Today" for the API is the Colombian date (UTC-5, no daylight saving time).
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private static DateOnly FirstOfMonth(int monthsFromNow) =>
        new DateOnly(Today.Year, Today.Month, 1).AddMonths(monthsFromNow);

    private static Task<JsonElement> ReadAsync(HttpResponseMessage response) => ApiTestHost.ReadAsync(response);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await ReadAsync(response)).GetProperty("code").GetString();

    /// <summary>Creates the monthly period of the month <paramref name="monthsFromNow"/> away (start day 1 by default).</summary>
    private static async Task<JsonElement> CreateMonthAsync(HttpClient client, int monthsFromNow)
    {
        var response = await client.PostAsJsonAsync(Periods, new { type = "Monthly", anchorDate = FirstOfMonth(monthsFromNow) }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<HttpResponseMessage> CloseAsync(HttpClient client, JsonElement period) =>
        await client.PostAsync($"{Periods}/{period.GetProperty("id").GetGuid()}/close", null, Ct);

    // --- Explicit creation (CP-20) ---

    [Fact]
    public async Task Asking_for_a_date_without_a_period_does_not_create_one()
    {
        using var session = await _host.SignInAsync();

        var current = await session.Client.GetAsync($"{Periods}/current", Ct);
        Assert.Equal(HttpStatusCode.NotFound, current.StatusCode);
        Assert.Equal("period.none_for_date", await CodeAsync(current));

        var proposal = await ReadAsync(await session.Client.GetAsync($"{Periods}/proposal", Ct));
        Assert.Equal("Monthly", proposal.GetProperty("type").GetString());
        Assert.Equal(FirstOfMonth(0).ToString("yyyy-MM-dd"), proposal.GetProperty("startDate").GetString());
        Assert.Equal(FirstOfMonth(1).AddDays(-1).ToString("yyyy-MM-dd"), proposal.GetProperty("endDate").GetString());
        Assert.False(proposal.GetProperty("overlapsExisting").GetBoolean());

        // Neither the lookup nor the proposal stored anything.
        Assert.Equal(0, (await ReadAsync(await session.Client.GetAsync(Periods, Ct))).GetArrayLength());
    }

    [Fact]
    public async Task A_period_created_without_dates_follows_the_settings_and_contains_today()
    {
        using var session = await _host.SignInAsync();
        var response = await session.Client.PostAsJsonAsync(Periods, new { }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var period = await ReadAsync(response);
        Assert.Equal("Monthly", period.GetProperty("type").GetString());
        Assert.Equal("Active", period.GetProperty("status").GetString());
        Assert.Equal("0", period.GetProperty("openingBalance").GetString());
        Assert.True(period.GetProperty("carryOverEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, period.GetProperty("closingBalance").ValueKind);

        var current = await ReadAsync(await session.Client.GetAsync($"{Periods}/current", Ct));
        Assert.Equal(period.GetProperty("id").GetGuid(), current.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_user_can_adjust_the_type_and_the_dates()
    {
        using var session = await _host.SignInAsync();
        var start = Today.AddDays(30);
        var response = await session.Client.PostAsJsonAsync(
            Periods, new { type = "Weekly", startDate = start, endDate = start.AddDays(9) }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var period = await ReadAsync(response);
        Assert.Equal("Weekly", period.GetProperty("type").GetString());
        Assert.Equal(start.ToString("yyyy-MM-dd"), period.GetProperty("startDate").GetString());
        Assert.Equal(start.AddDays(9).ToString("yyyy-MM-dd"), period.GetProperty("endDate").GetString());
        Assert.Equal("Planned", period.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invalid_ranges_are_rejected_with_400()
    {
        using var session = await _host.SignInAsync();
        var start = Today.AddDays(40);

        var backwards = await session.Client.PostAsJsonAsync(Periods, new { startDate = start, endDate = start.AddDays(-1) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal("period.invalid_range", await CodeAsync(backwards));

        var onlyStart = await session.Client.PostAsJsonAsync(Periods, new { startDate = start }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, onlyStart.StatusCode);
    }

    // --- Overlap (RF-PRD-03) ---

    [Fact]
    public async Task An_overlapping_period_is_rejected_with_409()
    {
        using var session = await _host.SignInAsync();
        await CreateMonthAsync(session.Client, 0);

        var overlapping = await session.Client.PostAsJsonAsync(
            Periods, new { type = "Weekly", startDate = FirstOfMonth(0).AddDays(10), endDate = FirstOfMonth(1).AddDays(3) }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, overlapping.StatusCode);
        Assert.Equal("period.overlap", await CodeAsync(overlapping));

        var proposal = await ReadAsync(await session.Client.GetAsync($"{Periods}/proposal", Ct));
        Assert.True(proposal.GetProperty("overlapsExisting").GetBoolean());
    }

    [Fact]
    public async Task Two_users_can_have_periods_on_the_same_dates()
    {
        using var ana = await _host.SignInAsync();
        using var luis = await _host.SignInAsync();
        await CreateMonthAsync(ana.Client, 0);
        await CreateMonthAsync(luis.Client, 0);
    }

    // --- Close and carry-over (CP-14) ---

    [Fact]
    public async Task Closing_with_a_positive_balance_carries_it_to_the_next_period()
    {
        using var session = await _host.SignInAsync();
        var previous = await CreateMonthAsync(session.Client, -2);
        Assert.Equal("Overdue", previous.GetProperty("status").GetString());
        await AddMovementsAsync(session.UserId, FirstOfMonth(-2).AddDays(4), received: 1_000_000m, paid: 400_000m);

        var close = await CloseAsync(session.Client, previous);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        var closed = await ReadAsync(close);
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.Equal("600000", closed.GetProperty("closingBalance").GetString());
        Assert.Equal("600000", closed.GetProperty("carriedOver").GetString());

        var next = await CreateMonthAsync(session.Client, -1);
        Assert.Equal("600000", next.GetProperty("openingBalance").GetString());

        // The closing figures are stored, not recomputed on read.
        var reread = await ReadAsync(await session.Client.GetAsync($"{Periods}/{previous.GetProperty("id").GetGuid()}", Ct));
        Assert.Equal("600000", reread.GetProperty("closingBalance").GetString());
    }

    [Fact]
    public async Task The_opening_balance_also_counts_in_the_next_closing()
    {
        using var session = await _host.SignInAsync();
        var first = await CreateMonthAsync(session.Client, -2);
        await AddMovementsAsync(session.UserId, FirstOfMonth(-2).AddDays(2), received: 500_000m, paid: 0m);
        await CloseAsync(session.Client, first);

        var second = await CreateMonthAsync(session.Client, -1);
        await AddMovementsAsync(session.UserId, FirstOfMonth(-1).AddDays(2), received: 0m, paid: 200_000m);
        var closed = await ReadAsync(await CloseAsync(session.Client, second));

        Assert.Equal("500000", closed.GetProperty("openingBalance").GetString());
        Assert.Equal("300000", closed.GetProperty("closingBalance").GetString());
    }

    [Fact]
    public async Task A_negative_balance_does_not_carry_over()
    {
        using var session = await _host.SignInAsync();
        var previous = await CreateMonthAsync(session.Client, -2);
        await AddMovementsAsync(session.UserId, FirstOfMonth(-2).AddDays(4), received: 100_000m, paid: 500_000m);

        var closed = await ReadAsync(await CloseAsync(session.Client, previous));
        Assert.Equal("-400000", closed.GetProperty("closingBalance").GetString());
        Assert.Equal("0", closed.GetProperty("carriedOver").GetString());
        Assert.Equal("0", (await CreateMonthAsync(session.Client, -1)).GetProperty("openingBalance").GetString());
    }

    [Fact]
    public async Task With_carry_over_disabled_the_balance_stays_in_the_period()
    {
        using var session = await _host.SignInAsync();
        var disable = await session.Client.PutAsJsonAsync("/api/v1/settings", new
        {
            primaryCurrency = "COP", language = "es", theme = "System", defaultPeriodType = "Monthly",
            carryOverEnabled = false, weekStart = "Monday", biweeklySecondCycleStartDay = 16, monthlyStartDay = 1,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var previous = await CreateMonthAsync(session.Client, -2);
        Assert.False(previous.GetProperty("carryOverEnabled").GetBoolean());
        await AddMovementsAsync(session.UserId, FirstOfMonth(-2).AddDays(4), received: 1_000_000m, paid: 0m);

        var closed = await ReadAsync(await CloseAsync(session.Client, previous));
        Assert.Equal("1000000", closed.GetProperty("closingBalance").GetString());
        Assert.Equal("0", closed.GetProperty("carriedOver").GetString());
    }

    [Fact]
    public async Task A_period_closes_only_once_and_only_after_it_starts()
    {
        using var session = await _host.SignInAsync();
        var current = await CreateMonthAsync(session.Client, 0);
        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(session.Client, current)).StatusCode);

        var again = await CloseAsync(session.Client, current);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("period.already_closed", await CodeAsync(again));

        var future = await CreateMonthAsync(session.Client, 2);
        var early = await CloseAsync(session.Client, future);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, early.StatusCode);
        Assert.Equal("period.not_started", await CodeAsync(early));
    }

    [Fact]
    public async Task Periods_close_in_order()
    {
        using var session = await _host.SignInAsync();
        // The later period first, then an earlier one filled in behind it: CP-21 only
        // blocks periods that start after an overdue one, so this order is allowed.
        var later = await CreateMonthAsync(session.Client, -2);
        await CreateMonthAsync(session.Client, -3);

        var response = await CloseAsync(session.Client, later);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("period.previous_open", await CodeAsync(response));
    }

    // --- Overdue blocks the next period (CP-21) ---

    [Fact]
    public async Task An_overdue_period_must_be_closed_before_the_next_one_is_created()
    {
        using var session = await _host.SignInAsync();
        var overdue = await CreateMonthAsync(session.Client, -1);
        Assert.Equal("Overdue", overdue.GetProperty("status").GetString());

        var blocked = await session.Client.PostAsJsonAsync(Periods, new { }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Equal("period.overdue_pending", await CodeAsync(blocked));

        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(session.Client, overdue)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync(Periods, new { }, Ct)).StatusCode);
    }

    // --- Access ---

    [Fact]
    public async Task Periods_need_a_verified_email()
    {
        using var unverified = await _host.SignInAsync(verifyEmail: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.Client.GetAsync(Periods, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client.GetAsync(Periods, Ct)).StatusCode);
    }

    [Fact]
    public async Task Another_users_period_is_not_found()
    {
        using var ana = await _host.SignInAsync();
        using var luis = await _host.SignInAsync();
        var anas = await CreateMonthAsync(ana.Client, -1);
        var id = anas.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await luis.Client.GetAsync($"{Periods}/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await luis.Client.PostAsync($"{Periods}/{id}/close", null, Ct)).StatusCode);
        Assert.Equal("Overdue", (await ReadAsync(await ana.Client.GetAsync($"{Periods}/{id}", Ct))).GetProperty("status").GetString());
    }

    /// <summary>
    /// Stores a received income and a paid expense on <paramref name="date"/>
    /// straight in the database as the user (the movement endpoints arrive in task 11).
    /// </summary>
    private async Task AddMovementsAsync(Guid userId, DateOnly date, decimal received, decimal paid)
    {
        await using var db = _host.DbAs(userId);
        if (received > 0)
        {
            db.IncomeTransactions.Add(new IncomeTransaction(
                Guid.NewGuid(), "Salario", new Money(received, Currency.Cop), date, "Empresa", IncomeStatus.Received, date));
        }

        if (paid > 0)
        {
            var categoryId = await db.ExpenseCategories.OrderBy(c => c.SortOrder).Select(c => c.Id).FirstAsync(Ct);
            var expense = new Expense(Guid.NewGuid(), "Arriendo", categoryId, new Money(paid, Currency.Cop), date, ExpenseType.Fixed);
            expense.AddPayment(new ExpensePayment(Guid.NewGuid(), new Money(paid, Currency.Cop), date));
            db.Expenses.Add(expense);
        }

        await db.SaveChangesAsync(Ct);
    }
}
