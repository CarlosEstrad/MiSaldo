using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Task 11 (🐳): incomes, expenses, payments and the dashboard over HTTP against
/// a real PostgreSQL. Movements are dated today, inside the current period.
/// Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class MovementEndpointsTests : IDisposable
{
    private const string Incomes = "/api/v1/incomes";
    private const string Expenses = "/api/v1/expenses";
    private const string Periods = "/api/v1/periods";
    private const string Dashboard = "/api/v1/dashboard";

    private readonly ApiTestHost _host;

    public MovementEndpointsTests(ApiPostgresFixture database) => _host = new ApiTestHost(database);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private static DateOnly FirstOfMonth(int monthsFromNow) =>
        new DateOnly(Today.Year, Today.Month, 1).AddMonths(monthsFromNow);

    private static Task<JsonElement> ReadAsync(HttpResponseMessage response) => ApiTestHost.ReadAsync(response);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await ReadAsync(response)).GetProperty("code").GetString();

    /// <summary>A verified session with the current monthly period already created.</summary>
    private async Task<ApiTestHost.Session> SessionWithCurrentPeriodAsync()
    {
        var session = await _host.SignInAsync();
        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync(Periods, new { }, Ct)).StatusCode);
        return session;
    }

    private static async Task<Guid> FirstCategoryAsync(HttpClient client) =>
        (await ReadAsync(await client.GetAsync("/api/v1/categories", Ct)))[0].GetProperty("id").GetGuid();

    private static async Task<JsonElement> CreateIncomeAsync(HttpClient client, string value, bool received, DateOnly? date = null)
    {
        var response = await client.PostAsJsonAsync(
            Incomes, new { description = "Salario", value, date = date ?? Today, source = "Empresa", received }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> CreateExpenseAsync(HttpClient client, string budgeted, DateOnly? date = null)
    {
        var response = await client.PostAsJsonAsync(Expenses, new
        {
            name = "Arriendo",
            categoryId = await FirstCategoryAsync(client),
            budgeted,
            date = date ?? Today,
            type = "Fixed",
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, JsonElement expense, string value, string? key = null, DateOnly? date = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Expenses}/{expense.GetProperty("id").GetGuid()}/payments")
        {
            Content = JsonContent.Create(new { value, date = date ?? Today }),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return client.SendAsync(request, Ct);
    }

    // --- 11.1 Incomes ---

    [Fact]
    public async Task An_income_is_registered_listed_edited_marked_received_and_deleted()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var income = await CreateIncomeAsync(session.Client, "1500000", received: false);
        var id = income.GetProperty("id").GetGuid();
        Assert.Equal("Planned", income.GetProperty("status").GetString());
        Assert.Equal("1500000", income.GetProperty("value").GetString());
        Assert.Equal("COP", income.GetProperty("currency").GetString());

        var list = await ReadAsync(await session.Client.GetAsync(Incomes, Ct));
        Assert.Equal(id, Assert.Single(list.EnumerateArray()).GetProperty("id").GetGuid());

        var edited = await session.Client.PutAsJsonAsync($"{Incomes}/{id}", new { description = "Salario neto", value = "1400000", date = Today, source = "Empresa" }, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("1400000", (await ReadAsync(edited)).GetProperty("value").GetString());

        var received = await ReadAsync(await session.Client.PostAsJsonAsync($"{Incomes}/{id}/receive", new { receivedDate = Today }, Ct));
        Assert.Equal("Received", received.GetProperty("status").GetString());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), received.GetProperty("receivedDate").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.DeleteAsync($"{Incomes}/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await session.Client.GetAsync($"{Incomes}/{id}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("0", "amount.not_positive")]
    [InlineData("-5000", "amount.not_positive")]
    [InlineData("abc", "amount.invalid")]
    [InlineData("1,500", "amount.invalid")]
    [InlineData("1500.5", "amount.too_many_decimals")]
    public async Task An_income_with_a_bad_value_is_rejected_with_400(string value, string code)
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var response = await session.Client.PostAsJsonAsync(
            Incomes, new { description = "Salario", value, date = Today, source = "Empresa", received = true }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
    }

    [Fact]
    public async Task A_number_instead_of_a_decimal_string_is_rejected()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var response = await session.Client.PostAsJsonAsync(
            Incomes, new { description = "Salario", value = 1500000.5, date = Today, source = "Empresa" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- 11.1 Expenses and payments ---

    [Fact]
    public async Task The_expense_status_is_derived_from_its_payments()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var expense = await CreateExpenseAsync(session.Client, "800000");
        Assert.Equal("Pending", expense.GetProperty("status").GetString());
        Assert.Equal("800000", expense.GetProperty("outstanding").GetString());

        var partial = await ReadAsync(await PayAsync(session.Client, expense, "500000"));
        Assert.Equal("Partial", partial.GetProperty("expense").GetProperty("status").GetString());
        Assert.Equal("300000", partial.GetProperty("expense").GetProperty("outstanding").GetString());

        // Paying more than budgeted is valid; nothing stays outstanding (RN-M06).
        var paid = await ReadAsync(await PayAsync(session.Client, expense, "400000"));
        Assert.Equal("Paid", paid.GetProperty("expense").GetProperty("status").GetString());
        Assert.Equal("900000", paid.GetProperty("expense").GetProperty("totalPaid").GetString());
        Assert.Equal("0", paid.GetProperty("expense").GetProperty("outstanding").GetString());
        Assert.Equal(2, paid.GetProperty("expense").GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task The_same_payment_sent_twice_with_the_same_key_is_stored_once()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var expense = await CreateExpenseAsync(session.Client, "800000");
        var key = Guid.NewGuid().ToString();

        var first = await PayAsync(session.Client, expense, "500000", key);
        var second = await PayAsync(session.Client, expense, "500000", key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", Assert.Single(second.Headers.GetValues("Idempotent-Replayed")));
        Assert.Equal(
            (await ReadAsync(first)).GetProperty("payment").GetProperty("id").GetGuid(),
            (await ReadAsync(second)).GetProperty("payment").GetProperty("id").GetGuid());

        var stored = await ReadAsync(await session.Client.GetAsync($"{Expenses}/{expense.GetProperty("id").GetGuid()}", Ct));
        Assert.Single(stored.GetProperty("payments").EnumerateArray());
        Assert.Equal("500000", stored.GetProperty("totalPaid").GetString());
    }

    [Fact]
    public async Task Two_simultaneous_payments_with_the_same_key_are_stored_once()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var expense = await CreateExpenseAsync(session.Client, "800000");
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PayAsync(session.Client, expense, "500000", key)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
        var stored = await ReadAsync(await session.Client.GetAsync($"{Expenses}/{expense.GetProperty("id").GetGuid()}", Ct));
        Assert.Single(stored.GetProperty("payments").EnumerateArray());
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_payment_is_a_conflict()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var expense = await CreateExpenseAsync(session.Client, "800000");
        var key = Guid.NewGuid().ToString();
        await PayAsync(session.Client, expense, "500000", key);

        var different = await PayAsync(session.Client, expense, "100000", key);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal("payment.idempotency_mismatch", await CodeAsync(different));
    }

    [Fact]
    public async Task Payments_without_a_key_are_each_stored()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var expense = await CreateExpenseAsync(session.Client, "800000");
        await PayAsync(session.Client, expense, "100000");
        var second = await ReadAsync(await PayAsync(session.Client, expense, "100000"));
        Assert.Equal(2, second.GetProperty("expense").GetProperty("payments").GetArrayLength());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task A_budget_or_payment_of_zero_or_less_is_rejected(string value)
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var badBudget = await session.Client.PostAsJsonAsync(Expenses, new
        {
            name = "Arriendo", categoryId = await FirstCategoryAsync(session.Client), budgeted = value, date = Today, type = "Fixed",
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badBudget.StatusCode);

        var expense = await CreateExpenseAsync(session.Client, "800000");
        Assert.Equal(HttpStatusCode.BadRequest, (await PayAsync(session.Client, expense, value)).StatusCode);
    }

    [Fact]
    public async Task An_expense_is_edited_and_one_without_payments_is_deleted()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var expense = await CreateExpenseAsync(session.Client, "800000");
        var id = expense.GetProperty("id").GetGuid();

        var edited = await session.Client.PutAsJsonAsync($"{Expenses}/{id}", new
        {
            name = "Arriendo apartamento", categoryId = expense.GetProperty("categoryId").GetGuid(), budgeted = "850000", date = Today, type = "Variable",
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var body = await ReadAsync(edited);
        Assert.Equal("Arriendo apartamento", body.GetProperty("name").GetString());
        Assert.Equal("850000", body.GetProperty("budgeted").GetString());
        Assert.Equal("Variable", body.GetProperty("type").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.DeleteAsync($"{Expenses}/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task An_expense_with_payments_keeps_its_history_and_is_not_deleted()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var expense = await CreateExpenseAsync(session.Client, "800000");
        await PayAsync(session.Client, expense, "100000");

        var response = await session.Client.DeleteAsync($"{Expenses}/{expense.GetProperty("id").GetGuid()}", Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("expense.has_payments", await CodeAsync(response));
    }

    [Fact]
    public async Task An_archived_or_foreign_category_cannot_be_used()
    {
        using var ana = await SessionWithCurrentPeriodAsync();
        using var luis = await SessionWithCurrentPeriodAsync();
        var anasCategory = await FirstCategoryAsync(ana.Client);

        var foreign = await luis.Client.PostAsJsonAsync(Expenses, new { name = "X", categoryId = anasCategory, budgeted = "1000", date = Today, type = "Fixed" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("expense.invalid_category", await CodeAsync(foreign));

        await ana.Client.PatchAsJsonAsync($"/api/v1/categories/{anasCategory}", new { isArchived = true }, Ct);
        var archived = await ana.Client.PostAsJsonAsync(Expenses, new { name = "X", categoryId = anasCategory, budgeted = "1000", date = Today, type = "Fixed" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, archived.StatusCode);
    }

    [Fact]
    public async Task Another_users_movements_are_not_found()
    {
        using var ana = await SessionWithCurrentPeriodAsync();
        using var luis = await SessionWithCurrentPeriodAsync();
        var income = await CreateIncomeAsync(ana.Client, "1000", received: true);
        var expense = await CreateExpenseAsync(ana.Client, "1000");
        var incomeId = income.GetProperty("id").GetGuid();
        var expenseId = expense.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await luis.Client.GetAsync($"{Incomes}/{incomeId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await luis.Client.DeleteAsync($"{Incomes}/{incomeId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await luis.Client.GetAsync($"{Expenses}/{expenseId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PayAsync(luis.Client, expense, "500")).StatusCode);
        Assert.Empty((await ReadAsync(await luis.Client.GetAsync(Incomes, Ct))).EnumerateArray());

        // Ana's data is untouched.
        Assert.Equal(HttpStatusCode.OK, (await ana.Client.GetAsync($"{Incomes}/{incomeId}", Ct)).StatusCode);
        Assert.Empty((await ReadAsync(await ana.Client.GetAsync($"{Expenses}/{expenseId}", Ct))).GetProperty("payments").EnumerateArray());
    }

    // --- Period rules on writes (RN-P04, CP-21) ---

    [Fact]
    public async Task Movements_in_a_closed_period_are_frozen()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var income = await CreateIncomeAsync(session.Client, "1000000", received: true);
        var expense = await CreateExpenseAsync(session.Client, "200000");
        var period = await ReadAsync(await session.Client.GetAsync($"{Periods}/current", Ct));
        Assert.Equal(HttpStatusCode.OK, (await session.Client.PostAsync($"{Periods}/{period.GetProperty("id").GetGuid()}/close", null, Ct)).StatusCode);

        var create = await session.Client.PostAsJsonAsync(Incomes, new { description = "Extra", value = "1000", date = Today, source = "X" }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, create.StatusCode);
        Assert.Equal("period.closed", await CodeAsync(create));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await session.Client.DeleteAsync($"{Incomes}/{income.GetProperty("id").GetGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PayAsync(session.Client, expense, "1000")).StatusCode);
    }

    [Fact]
    public async Task Nothing_after_an_overdue_period_changes_until_it_is_closed()
    {
        using var session = await _host.SignInAsync();
        var overdue = await ReadAsync(await session.Client.PostAsJsonAsync(Periods, new { type = "Monthly", anchorDate = FirstOfMonth(-1) }, Ct));
        Assert.Equal("Overdue", overdue.GetProperty("status").GetString());

        // Inside the overdue period itself the user can still finish registering.
        await CreateIncomeAsync(session.Client, "1000", received: true, FirstOfMonth(-1).AddDays(3));

        var later = await session.Client.PostAsJsonAsync(Incomes, new { description = "Hoy", value = "1000", date = Today, source = "X" }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, later.StatusCode);
        Assert.Equal("period.overdue_pending", await CodeAsync(later));
    }

    // --- Currency per user (design T2) ---

    [Fact]
    public async Task A_user_with_dollars_stores_and_reads_amounts_with_cents()
    {
        using var session = await _host.SignInAsync();
        var usd = await session.Client.PutAsJsonAsync("/api/v1/settings", new
        {
            primaryCurrency = "USD", language = "en", theme = "System", defaultPeriodType = "Monthly",
            carryOverEnabled = true, weekStart = "Monday", biweeklySecondCycleStartDay = 16, monthlyStartDay = 1,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, usd.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync(Periods, new { }, Ct)).StatusCode);

        var income = await CreateIncomeAsync(session.Client, "1250.75", received: true);
        Assert.Equal("1250.75", income.GetProperty("value").GetString());
        Assert.Equal("USD", income.GetProperty("currency").GetString());
        var expense = await CreateExpenseAsync(session.Client, "300.50");
        await PayAsync(session.Client, expense, "100.25");

        var dashboard = await ReadAsync(await session.Client.GetAsync(Dashboard, Ct));
        Assert.Equal("USD", dashboard.GetProperty("currency").GetString());
        Assert.Equal("1150.50", dashboard.GetProperty("cashOnHand").GetString());
        Assert.Equal("200.25", dashboard.GetProperty("reserved").GetString());
        Assert.Equal("950.25", dashboard.GetProperty("freeMoney").GetString());

        var tooPrecise = await session.Client.PostAsJsonAsync(Incomes, new { description = "X", value = "1.005", date = Today, source = "X" }, Ct);
        Assert.Equal("amount.too_many_decimals", await CodeAsync(tooPrecise));
    }

    // --- 11.2 Dashboard ---

    [Fact]
    public async Task Cp01_cash_is_received_income_minus_paid_expense()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        await CreateIncomeAsync(session.Client, "2000000", received: true);
        var expense = await CreateExpenseAsync(session.Client, "500000");
        await PayAsync(session.Client, expense, "500000");

        var dashboard = await ReadAsync(await session.Client.GetAsync(Dashboard, Ct));
        Assert.Equal("2000000", dashboard.GetProperty("receivedIncome").GetString());
        Assert.Equal("500000", dashboard.GetProperty("paidExpense").GetString());
        Assert.Equal("1500000", dashboard.GetProperty("cashOnHand").GetString());
        Assert.Equal(0.25m, dashboard.GetProperty("budgetConsumption").GetDecimal());
    }

    [Fact]
    public async Task Cp02_free_money_subtracts_the_outstanding_obligation()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        await CreateIncomeAsync(session.Client, "2000000", received: true);
        var expense = await CreateExpenseAsync(session.Client, "800000");
        await PayAsync(session.Client, expense, "500000");

        var dashboard = await ReadAsync(await session.Client.GetAsync(Dashboard, Ct));
        Assert.Equal("1500000", dashboard.GetProperty("cashOnHand").GetString());
        Assert.Equal("300000", dashboard.GetProperty("outstandingObligations").GetString());
        Assert.Equal("300000", dashboard.GetProperty("reserved").GetString());
        Assert.Equal("1200000", dashboard.GetProperty("freeMoney").GetString());
        Assert.False(dashboard.GetProperty("freeMoneyIsNegative").GetBoolean());
    }

    [Fact]
    public async Task Cp13_a_planned_income_does_not_count_as_cash()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        await CreateIncomeAsync(session.Client, "2000000", received: false);

        var dashboard = await ReadAsync(await session.Client.GetAsync(Dashboard, Ct));
        Assert.Equal("0", dashboard.GetProperty("receivedIncome").GetString());
        Assert.Equal("0", dashboard.GetProperty("cashOnHand").GetString());
    }

    [Fact]
    public async Task Negative_free_money_is_shown_as_it_is()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        await CreateIncomeAsync(session.Client, "100000", received: true);
        await CreateExpenseAsync(session.Client, "400000");

        var dashboard = await ReadAsync(await session.Client.GetAsync(Dashboard, Ct));
        Assert.Equal("-300000", dashboard.GetProperty("freeMoney").GetString());
        Assert.True(dashboard.GetProperty("freeMoneyIsNegative").GetBoolean());
    }

    [Fact]
    public async Task Each_change_shows_up_in_the_next_dashboard_read()
    {
        using var session = await SessionWithCurrentPeriodAsync();
        var income = await CreateIncomeAsync(session.Client, "1000000", received: false);
        Assert.Equal("0", (await ReadAsync(await session.Client.GetAsync(Dashboard, Ct))).GetProperty("cashOnHand").GetString());

        await session.Client.PostAsJsonAsync($"{Incomes}/{income.GetProperty("id").GetGuid()}/receive", new { }, Ct);
        Assert.Equal("1000000", (await ReadAsync(await session.Client.GetAsync(Dashboard, Ct))).GetProperty("cashOnHand").GetString());
    }

    [Fact]
    public async Task The_dashboard_shows_the_carry_over_from_the_previous_period()
    {
        using var session = await _host.SignInAsync();
        var previous = await ReadAsync(await session.Client.PostAsJsonAsync(Periods, new { type = "Monthly", anchorDate = FirstOfMonth(-1) }, Ct));
        await CreateIncomeAsync(session.Client, "700000", received: true, FirstOfMonth(-1).AddDays(2));
        await session.Client.PostAsync($"{Periods}/{previous.GetProperty("id").GetGuid()}/close", null, Ct);
        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync(Periods, new { }, Ct)).StatusCode);

        var dashboard = await ReadAsync(await session.Client.GetAsync(Dashboard, Ct));
        Assert.Equal("700000", dashboard.GetProperty("carryOver").GetString());
        Assert.Equal("700000", dashboard.GetProperty("cashOnHand").GetString());

        var ofPrevious = await ReadAsync(await session.Client.GetAsync($"{Dashboard}?periodId={previous.GetProperty("id").GetGuid()}", Ct));
        Assert.Equal("700000", ofPrevious.GetProperty("receivedIncome").GetString());
    }

    [Fact]
    public async Task Without_a_period_for_today_the_dashboard_is_404_and_nothing_is_created()
    {
        using var session = await _host.SignInAsync();
        var response = await session.Client.GetAsync(Dashboard, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("period.none_for_date", await CodeAsync(response));
        Assert.Empty((await ReadAsync(await session.Client.GetAsync(Periods, Ct))).EnumerateArray());
    }

    [Fact]
    public async Task Movements_and_the_dashboard_need_a_verified_email()
    {
        using var unverified = await _host.SignInAsync(verifyEmail: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.Client.GetAsync(Incomes, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.Client.GetAsync(Expenses, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.Client.GetAsync(Dashboard, Ct)).StatusCode);
    }
}
