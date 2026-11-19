using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiSaldo.Domain.Common;
using MiSaldo.Domain.Expenses;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Task 9 (🐳): profile, settings and categories over HTTP against a real
/// PostgreSQL. Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class ProfileSettingsCategoriesTests : IDisposable
{
    private const string Profile = "/api/v1/profile";
    private const string Settings = "/api/v1/settings";
    private const string Categories = "/api/v1/categories";

    private readonly ApiTestHost _host;

    public ProfileSettingsCategoriesTests(ApiPostgresFixture database) => _host = new ApiTestHost(database);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<JsonElement> ReadAsync(HttpResponseMessage response) => ApiTestHost.ReadAsync(response);

    private static object SettingsBody(
        string currency = "COP",
        string language = "en",
        string theme = "Dark",
        string periodType = "Biweekly",
        bool carryOver = false,
        string weekStart = "Sunday",
        int biweeklyDay = 15,
        int monthlyDay = 5) => new
        {
            primaryCurrency = currency,
            language,
            theme,
            defaultPeriodType = periodType,
            carryOverEnabled = carryOver,
            weekStart,
            biweeklySecondCycleStartDay = biweeklyDay,
            monthlyStartDay = monthlyDay,
        };

    // --- Profile ---

    [Fact]
    public async Task The_profile_is_read_and_edited_but_the_birth_date_and_email_stay()
    {
        using var session = await _host.SignInAsync();

        var initial = await ReadAsync(await session.Client.GetAsync(Profile, Ct));
        Assert.Equal("Ana Perez", initial.GetProperty("fullName").GetString());
        Assert.Equal(session.Email, initial.GetProperty("email").GetString());
        Assert.Equal("1990-05-01", initial.GetProperty("birthDate").GetString());

        // Extra fields such as birthDate or email are ignored: they are not part of the editable profile.
        var update = await session.Client.PutAsJsonAsync(Profile, new
        {
            fullName = "  Ana María Pérez ",
            country = "mx",
            gender = "female",
            birthDate = "2001-01-01",
            email = "someone-else@example.com",
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var reread = await ReadAsync(await session.Client.GetAsync(Profile, Ct));
        Assert.Equal("Ana María Pérez", reread.GetProperty("fullName").GetString());
        Assert.Equal("MX", reread.GetProperty("country").GetString());
        Assert.Equal("female", reread.GetProperty("gender").GetString());
        Assert.Equal("1990-05-01", reread.GetProperty("birthDate").GetString());
        Assert.Equal(session.Email, reread.GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("", "CO")]
    [InlineData("Ana", "Colombia")]
    public async Task An_invalid_profile_update_is_rejected_with_400(string fullName, string country)
    {
        using var session = await _host.SignInAsync();
        var response = await session.Client.PutAsJsonAsync(Profile, new { fullName, country }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Profile_and_settings_need_a_session_but_not_a_verified_email()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client.GetAsync(Profile, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Client.GetAsync(Settings, Ct)).StatusCode);

        using var unverified = await _host.SignInAsync(verifyEmail: false);
        Assert.Equal(HttpStatusCode.OK, (await unverified.Client.GetAsync(Profile, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await unverified.Client.GetAsync(Settings, Ct)).StatusCode);
    }

    // --- Settings ---

    [Fact]
    public async Task New_accounts_start_with_the_default_settings()
    {
        using var session = await _host.SignInAsync();
        var settings = await ReadAsync(await session.Client.GetAsync(Settings, Ct));

        Assert.Equal("COP", settings.GetProperty("primaryCurrency").GetString());
        Assert.Equal("es", settings.GetProperty("language").GetString());
        Assert.Equal("System", settings.GetProperty("theme").GetString());
        Assert.Equal("Monthly", settings.GetProperty("defaultPeriodType").GetString());
        Assert.True(settings.GetProperty("carryOverEnabled").GetBoolean());
    }

    [Fact]
    public async Task Settings_persist_on_the_server()
    {
        using var session = await _host.SignInAsync();
        var update = await session.Client.PutAsJsonAsync(Settings, SettingsBody(currency: "usd"), Ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        // Read back in a separate request: the values come from the database, not from the update response.
        var settings = await ReadAsync(await session.Client.GetAsync(Settings, Ct));
        Assert.Equal("USD", settings.GetProperty("primaryCurrency").GetString());
        Assert.Equal("en", settings.GetProperty("language").GetString());
        Assert.Equal("Dark", settings.GetProperty("theme").GetString());
        Assert.Equal("Biweekly", settings.GetProperty("defaultPeriodType").GetString());
        Assert.False(settings.GetProperty("carryOverEnabled").GetBoolean());
        Assert.Equal("Sunday", settings.GetProperty("weekStart").GetString());
        Assert.Equal(15, settings.GetProperty("biweeklySecondCycleStartDay").GetInt32());
        Assert.Equal(5, settings.GetProperty("monthlyStartDay").GetInt32());
    }

    [Theory]
    [InlineData("EUR", "en", 16, 1, "settings.unsupported_currency")]
    [InlineData("COP", "fr", 16, 1, "settings.invalid_language")]
    [InlineData("COP", "es", 1, 1, "settings.invalid_period_day")]
    [InlineData("COP", "es", 16, 29, "settings.invalid_period_day")]
    public async Task Invalid_settings_are_rejected_with_400(
        string currency, string language, int biweeklyDay, int monthlyDay, string code)
    {
        using var session = await _host.SignInAsync();
        var response = await session.Client.PutAsJsonAsync(
            Settings, SettingsBody(currency, language, biweeklyDay: biweeklyDay, monthlyDay: monthlyDay), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Neon")]
    [InlineData("2")]
    public async Task An_unknown_option_name_or_a_number_is_rejected(string theme)
    {
        using var session = await _host.SignInAsync();
        var body = theme == "2"
            ? (object)new
            {
                primaryCurrency = "COP", language = "es", theme = 2, defaultPeriodType = "Monthly",
                carryOverEnabled = true, weekStart = "Monday", biweeklySecondCycleStartDay = 16, monthlyStartDay = 1,
            }
            : SettingsBody(theme: theme);

        var response = await session.Client.PutAsJsonAsync(Settings, body, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_currency_is_locked_once_there_are_movements()
    {
        using var session = await _host.SignInAsync();
        await AddExpenseAsync(session.UserId);

        var change = await session.Client.PutAsJsonAsync(Settings, SettingsBody(currency: "USD"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        Assert.Equal("settings.currency_locked", (await ReadAsync(change)).GetProperty("code").GetString());

        // Keeping the same currency still lets the other preferences change.
        var same = await session.Client.PutAsJsonAsync(Settings, SettingsBody(currency: "COP"), Ct);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal("COP", (await ReadAsync(same)).GetProperty("primaryCurrency").GetString());
    }

    // --- Categories ---

    [Fact]
    public async Task New_accounts_have_the_starting_categories()
    {
        using var session = await _host.SignInAsync();
        var categories = await ReadAsync(await session.Client.GetAsync(Categories, Ct));
        Assert.Equal(10, categories.GetArrayLength());
    }

    [Fact]
    public async Task Categories_need_a_verified_email()
    {
        using var unverified = await _host.SignInAsync(verifyEmail: false);
        var response = await unverified.Client.GetAsync(Categories, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_category_is_created_renamed_reordered_and_archived()
    {
        using var session = await _host.SignInAsync();
        var created = await session.Client.PostAsJsonAsync(Categories, new { name = "  Mascotas " }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var category = await ReadAsync(created);
        var id = category.GetProperty("id").GetGuid();
        Assert.Equal("Mascotas", category.GetProperty("name").GetString());
        Assert.Equal(11, category.GetProperty("sortOrder").GetInt32()); // after the starting ones, numbered 1 to 10

        var renamed = await ReadAsync(await session.Client.PatchAsJsonAsync($"{Categories}/{id}", new { name = "Mascotas y veterinario", sortOrder = 0 }, Ct));
        Assert.Equal("Mascotas y veterinario", renamed.GetProperty("name").GetString());
        Assert.Equal(0, renamed.GetProperty("sortOrder").GetInt32());

        var archived = await session.Client.PatchAsJsonAsync($"{Categories}/{id}", new { isArchived = true }, Ct);
        Assert.True((await ReadAsync(archived)).GetProperty("isArchived").GetBoolean());

        var active = await ReadAsync(await session.Client.GetAsync(Categories, Ct));
        Assert.DoesNotContain(active.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        var all = await ReadAsync(await session.Client.GetAsync($"{Categories}?includeArchived=true", Ct));
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
    }

    [Theory]
    [InlineData("Viajes", "viajes")]
    [InlineData("Viajes", "  VIAJES ")]
    public async Task A_duplicate_category_name_is_rejected_with_409_ignoring_case(string first, string second)
    {
        using var session = await _host.SignInAsync();
        Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync(Categories, new { name = first }, Ct)).StatusCode);

        var duplicate = await session.Client.PostAsJsonAsync(Categories, new { name = second }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("category.duplicate_name", (await ReadAsync(duplicate)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Renaming_to_an_existing_name_is_rejected_but_keeping_the_own_name_is_not()
    {
        using var session = await _host.SignInAsync();
        var a = (await ReadAsync(await session.Client.PostAsJsonAsync(Categories, new { name = "Uno" }, Ct))).GetProperty("id").GetGuid();
        await session.Client.PostAsJsonAsync(Categories, new { name = "Dos" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, (await session.Client.PatchAsJsonAsync($"{Categories}/{a}", new { name = "dos" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.PatchAsJsonAsync($"{Categories}/{a}", new { name = "UNO" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_same_category_name_is_allowed_for_different_users()
    {
        using var ana = await _host.SignInAsync();
        using var luis = await _host.SignInAsync();
        Assert.Equal(HttpStatusCode.Created, (await ana.Client.PostAsJsonAsync(Categories, new { name = "Gimnasio" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await luis.Client.PostAsJsonAsync(Categories, new { name = "Gimnasio" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_category_without_movements_is_deleted_and_one_with_movements_is_archived()
    {
        using var session = await _host.SignInAsync();
        var unused = (await ReadAsync(await session.Client.PostAsJsonAsync(Categories, new { name = "Sin uso" }, Ct))).GetProperty("id").GetGuid();
        var used = await AddExpenseAsync(session.UserId);

        var deleted = await ReadAsync(await session.Client.DeleteAsync($"{Categories}/{unused}", Ct));
        Assert.Equal("Deleted", deleted.GetProperty("outcome").GetString());

        var archived = await ReadAsync(await session.Client.DeleteAsync($"{Categories}/{used}", Ct));
        Assert.Equal("Archived", archived.GetProperty("outcome").GetString());

        var all = await ReadAsync(await session.Client.GetAsync($"{Categories}?includeArchived=true", Ct));
        Assert.DoesNotContain(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == unused);
        var kept = Assert.Single(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == used);
        Assert.True(kept.GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task Another_users_category_is_not_found()
    {
        using var ana = await _host.SignInAsync();
        using var luis = await _host.SignInAsync();
        var anas = (await ReadAsync(await ana.Client.PostAsJsonAsync(Categories, new { name = "Privada" }, Ct))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await luis.Client.PatchAsJsonAsync($"{Categories}/{anas}", new { name = "Robada" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await luis.Client.DeleteAsync($"{Categories}/{anas}", Ct)).StatusCode);
        var luisList = await ReadAsync(await luis.Client.GetAsync($"{Categories}?includeArchived=true", Ct));
        Assert.DoesNotContain(luisList.EnumerateArray(), c => c.GetProperty("id").GetGuid() == anas);

        // Ana's category is untouched.
        var anasList = await ReadAsync(await ana.Client.GetAsync(Categories, Ct));
        Assert.Contains(anasList.EnumerateArray(), c => c.GetProperty("name").GetString() == "Privada");
    }

    [Fact]
    public async Task An_empty_category_name_is_rejected_with_400()
    {
        using var session = await _host.SignInAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await session.Client.PostAsJsonAsync(Categories, new { name = "   " }, Ct)).StatusCode);
    }

    /// <summary>Stores an expense for the user straight in the database (the expense endpoints arrive in task 11). Returns its category.</summary>
    private async Task<Guid> AddExpenseAsync(Guid userId)
    {
        await using var db = _host.DbAs(userId);
        var categoryId = await db.ExpenseCategories.OrderBy(c => c.SortOrder).Select(c => c.Id).FirstAsync(Ct);
        db.Expenses.Add(new Expense(
            Guid.NewGuid(), "Arriendo", categoryId, new Money(1_000_000m, Currency.Cop), new DateOnly(2026, 1, 5), ExpenseType.Fixed));
        await db.SaveChangesAsync(Ct);
        return categoryId;
    }
}
