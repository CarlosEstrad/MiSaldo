using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MiSaldo.Api.Common;
using MiSaldo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// The API hosted over the test PostgreSQL, with cheap hashing and a high
/// request budget, plus helpers to open a signed-in session through HTTP.
/// </summary>
public sealed class ApiTestHost : IDisposable
{
    public const string Password = "a long enough password";

    private readonly ApiPostgresFixture _database;
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTestHost(ApiPostgresFixture database, int maxFailedLoginAttempts = 5, Action<IWebHostBuilder>? configure = null)
    {
        _database = database;
        _factory = ApiFactory.With(ApiFactory.TestSigningKey, database.AppConnectionString)
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Argon2:MemorySizeKiB", "1024");
                builder.UseSetting("Argon2:Iterations", "1");
                builder.UseSetting("Argon2:Parallelism", "1");
                builder.UseSetting(
                    "Authentication:MaxFailedLoginAttempts", maxFailedLoginAttempts.ToString(CultureInfo.InvariantCulture));
                builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");
                builder.UseSetting("RateLimiting:GeneralPermitLimit", "1000");
                builder.ConfigureServices(services => services
                    .AddControllers()
                    .AddApplicationPart(typeof(WhoAmITestController).Assembly));
                configure?.Invoke(builder);
            });
        Client = CreateClient();
    }

    /// <summary>A client on https://localhost that keeps cookies, like a browser tab.</summary>
    public HttpClient Client { get; }

    /// <summary>The application's services, for tests that inspect the host (endpoints, OpenAPI).</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>
    /// A new client without credentials on https://localhost (the refresh cookie is
    /// <c>Secure</c>). Without cookie handling, a test sends the cookie it wants by hand.
    /// </summary>
    public HttpClient CreateClient(bool handleCookies = true) => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = SessionCookies.BaseAddress,
        HandleCookies = handleCookies,
    });

    /// <summary>POSTs to an auth route with exactly this refresh token in the cookie.</summary>
    public async Task<HttpResponseMessage> PostWithRefreshCookieAsync(string path, string? refreshToken)
    {
        using var client = CreateClient(handleCookies: false);
        using var request = SessionCookies.PostWithRefreshCookie(path, refreshToken);
        return await client.SendAsync(request, Ct);
    }

    public InMemoryAccountNotifier Notifier => _factory.Services.GetRequiredService<InMemoryAccountNotifier>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static object Registration(string email, DateOnly? birthDate = null, string password = Password) => new
    {
        email,
        password,
        fullName = "Ana Perez",
        birthDate = birthDate ?? new DateOnly(1990, 5, 1),
        country = "CO",
        acceptsPrivacyPolicy = true,
        acceptsTerms = true,
    };

    /// <summary>Registers, optionally verifies the email, and logs in. Returns a client that sends the access token.</summary>
    public async Task<Session> SignInAsync(bool verifyEmail = true)
    {
        var email = UniqueEmail();
        var register = await Client.PostAsJsonAsync($"/{ApiRoutes.V1}/auth/register", Registration(email), Ct);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        if (verifyEmail)
        {
            var token = Notifier.LatestEmailVerification(email)!.Token;
            var verify = await Client.PostAsJsonAsync($"/{ApiRoutes.V1}/auth/verify-email", new { token }, Ct);
            Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
        }

        var login = await Client.PostAsJsonAsync($"/{ApiRoutes.V1}/auth/login", new { email, password = Password }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = await ReadAsync(login);

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return new Session(tokens.GetProperty("userId").GetGuid(), email, client);
    }

    /// <summary>A database context acting as the given user (filter and RLS apply), for setup the API cannot do yet.</summary>
    public MiSaldoDbContext DbAs(Guid userId)
    {
        var currentUser = new FixedUser(userId);
        var options = new DbContextOptionsBuilder<MiSaldoDbContext>();
        MiSaldoDbContextConfigurator.Configure(options, _database.AppConnectionString, currentUser);
        return new MiSaldoDbContext(options.Options, currentUser);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }

    public sealed record Session(Guid UserId, string Email, HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private sealed class FixedUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
    }
}
