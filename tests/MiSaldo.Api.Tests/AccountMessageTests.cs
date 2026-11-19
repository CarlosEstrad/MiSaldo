using System.Net;
using System.Net.Http.Json;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Spec 04, task 2 (🐳): the registration accepts a language, stores it in the
/// settings, and the account messages carry the user's language and the moment
/// their link expires. Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class AccountMessageTests : IDisposable
{
    private const string Register = "/api/v1/auth/register";

    private readonly ApiTestHost _host;

    public AccountMessageTests(ApiPostgresFixture database) => _host = new ApiTestHost(database);

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Registration(string email, string? language) => new
    {
        email,
        password = ApiTestHost.Password,
        fullName = "Ana Perez",
        birthDate = new DateOnly(1990, 5, 1),
        country = "CO",
        acceptsPrivacyPolicy = true,
        acceptsTerms = true,
        language,
    };

    private async Task<string> RegisterAsync(string? language)
    {
        var email = ApiTestHost.UniqueEmail();
        var response = await _host.Client.PostAsJsonAsync(Register, Registration(email, language), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    private async Task<string?> SettingsLanguageAsync(string email)
    {
        var token = _host.Notifier.LatestEmailVerification(email)!.Token;
        await _host.Client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token }, Ct);
        var login = await ApiTestHost.ReadAsync(
            await _host.Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = ApiTestHost.Password }, Ct));

        using var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.GetProperty("accessToken").GetString());
        return (await ApiTestHost.ReadAsync(await client.GetAsync("/api/v1/settings", Ct))).GetProperty("language").GetString();
    }

    [Fact]
    public async Task The_registration_language_is_stored_and_used_for_the_verification_message()
    {
        var before = DateTimeOffset.UtcNow;
        var email = await RegisterAsync(" EN ");

        var message = _host.Notifier.LatestEmailVerification(email);
        Assert.NotNull(message);
        Assert.Equal("en", message.Language);

        // The verification link lasts 24 hours (RegistrationOptions).
        Assert.InRange(message.ExpiresAt, before.AddHours(24).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(24).AddMinutes(1));
        Assert.Equal("en", await SettingsLanguageAsync(email));
    }

    [Fact]
    public async Task Without_a_language_the_registration_uses_spanish()
    {
        var email = await RegisterAsync(null);
        Assert.Equal("es", _host.Notifier.LatestEmailVerification(email)!.Language);
        Assert.Equal("es", await SettingsLanguageAsync(email));
    }

    [Fact]
    public async Task An_unsupported_language_is_rejected_and_no_account_is_created()
    {
        var email = ApiTestHost.UniqueEmail();
        var response = await _host.Client.PostAsJsonAsync(Register, Registration(email, "fr"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("registration.invalid_language", (await ApiTestHost.ReadAsync(response)).GetProperty("code").GetString());
        Assert.Null(_host.Notifier.LatestEmailVerification(email));
    }

    [Fact]
    public async Task The_reset_message_follows_the_language_in_the_settings()
    {
        using var session = await _host.SignInAsync();
        var toEnglish = await session.Client.PutAsJsonAsync("/api/v1/settings", new
        {
            primaryCurrency = "COP", language = "en", theme = "System", defaultPeriodType = "Monthly",
            carryOverEnabled = true, weekStart = "Monday", biweeklySecondCycleStartDay = 16, monthlyStartDay = 1,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, toEnglish.StatusCode);

        var before = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.Accepted, (await _host.Client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password", new { email = session.Email }, Ct)).StatusCode);

        var message = _host.Notifier.LatestPasswordReset(session.Email);
        Assert.NotNull(message);
        Assert.Equal("en", message.Language);

        // The reset link lasts one hour (AuthenticationOptions).
        Assert.InRange(message.ExpiresAt, before.AddHours(1).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1).AddMinutes(1));
    }
}
