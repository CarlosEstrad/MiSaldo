using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MiSaldo.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>
/// Task 12 (🐳): the closing end-to-end suite over HTTP against a real
/// PostgreSQL (R15). A whole user journey, isolation between users on every
/// resource (CP-07), 401 on every private route, the OpenAPI document, Problem
/// Details for each error status and logs free of secrets (R14.1, R14.4).
/// Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class EndToEndTests : IDisposable
{
    private const string V1 = "/api/v1";

    private readonly ApiPostgresFixture _database;
    private readonly ApiTestHost _host;

    public EndToEndTests(ApiPostgresFixture database)
    {
        _database = database;
        _host = new ApiTestHost(database);
    }

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-5));

    private static Task<JsonElement> ReadAsync(HttpResponseMessage response) => ApiTestHost.ReadAsync(response);

    private static async Task<JsonElement> OkJsonAsync(Task<HttpResponseMessage> request, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await request;
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return await ReadAsync(response);
    }

    private static HttpClient WithToken(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    // --- R15.2 + R15.4: one user, start to finish ---

    [Fact]
    public async Task A_user_signs_up_manages_money_and_signs_out_over_http()
    {
        var anonymous = _host.Client;
        var email = ApiTestHost.UniqueEmail();

        // Sign up and verify the email.
        await OkJsonAsync(anonymous.PostAsJsonAsync($"{V1}/auth/register", ApiTestHost.Registration(email), Ct), HttpStatusCode.Created);
        var verification = _host.Notifier.LatestEmailVerification(email)!.Token;
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync($"{V1}/auth/verify-email", new { token = verification }, Ct)).StatusCode);

        // Sign in; the access token opens the private routes.
        var tokens = await OkJsonAsync(anonymous.PostAsJsonAsync($"{V1}/auth/login", new { email, password = ApiTestHost.Password }, Ct));
        using var client = WithToken(_host.CreateClient(), tokens.GetProperty("accessToken").GetString()!);
        Assert.Equal(email, (await OkJsonAsync(client.GetAsync($"{V1}/profile", Ct))).GetProperty("email").GetString());

        // A period, an income, an expense with a payment: the dashboard reflects them (CP-01, CP-02).
        var period = await OkJsonAsync(client.PostAsJsonAsync($"{V1}/periods", new { }, Ct), HttpStatusCode.Created);
        var categoryId = (await OkJsonAsync(client.GetAsync($"{V1}/categories", Ct)))[0].GetProperty("id").GetGuid();
        await OkJsonAsync(client.PostAsJsonAsync($"{V1}/incomes",
            new { description = "Salario", value = "2000000", date = Today, source = "Empresa", received = true }, Ct), HttpStatusCode.Created);
        var expense = await OkJsonAsync(client.PostAsJsonAsync($"{V1}/expenses",
            new { name = "Arriendo", categoryId, budgeted = "800000", date = Today, type = "Fixed" }, Ct), HttpStatusCode.Created);
        await OkJsonAsync(client.PostAsJsonAsync($"{V1}/expenses/{expense.GetProperty("id").GetGuid()}/payments",
            new { value = "500000", date = Today }, Ct), HttpStatusCode.Created);

        var dashboard = await OkJsonAsync(client.GetAsync($"{V1}/dashboard", Ct));
        Assert.Equal(period.GetProperty("id").GetGuid(), dashboard.GetProperty("period").GetProperty("id").GetGuid());
        Assert.Equal("1500000", dashboard.GetProperty("cashOnHand").GetString());
        Assert.Equal("1200000", dashboard.GetProperty("freeMoney").GetString());

        // Refresh rotates the session; the new access token works too.
        var refreshed = await OkJsonAsync(anonymous.PostAsJsonAsync($"{V1}/auth/refresh",
            new { refreshToken = tokens.GetProperty("refreshToken").GetString() }, Ct));
        using var renewed = WithToken(_host.CreateClient(), refreshed.GetProperty("accessToken").GetString()!);
        Assert.Equal("1500000", (await OkJsonAsync(renewed.GetAsync($"{V1}/dashboard", Ct))).GetProperty("cashOnHand").GetString());

        // Sign out: the refresh token is dead.
        var refreshToken = refreshed.GetProperty("refreshToken").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync($"{V1}/auth/logout", new { refreshToken }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"{V1}/auth/refresh", new { refreshToken }, Ct)).StatusCode);
    }

    // --- R15.3: CP-07 on every resource ---

    [Fact]
    public async Task Cp07_a_user_never_reads_or_changes_another_users_data()
    {
        using var ana = await _host.SignInAsync();
        using var luis = await _host.SignInAsync();

        // Ana's data.
        var period = await OkJsonAsync(ana.Client.PostAsJsonAsync($"{V1}/periods", new { }, Ct), HttpStatusCode.Created);
        var category = await OkJsonAsync(ana.Client.PostAsJsonAsync($"{V1}/categories", new { name = "Privada de Ana" }, Ct), HttpStatusCode.Created);
        var income = await OkJsonAsync(ana.Client.PostAsJsonAsync($"{V1}/incomes",
            new { description = "Salario", value = "2000000", date = Today, source = "Empresa", received = true }, Ct), HttpStatusCode.Created);
        var expense = await OkJsonAsync(ana.Client.PostAsJsonAsync($"{V1}/expenses",
            new { name = "Arriendo", categoryId = category.GetProperty("id").GetGuid(), budgeted = "800000", date = Today, type = "Fixed" }, Ct), HttpStatusCode.Created);
        var anaDashboardBefore = await ReadAsync(await ana.Client.GetAsync($"{V1}/dashboard", Ct));

        var periodId = period.GetProperty("id").GetGuid();
        var categoryId = category.GetProperty("id").GetGuid();
        var incomeId = income.GetProperty("id").GetGuid();
        var expenseId = expense.GetProperty("id").GetGuid();

        // Luis, with a valid session of his own, aims at each of Ana's resources by id.
        var attempts = new (string Name, Func<Task<HttpResponseMessage>> Call)[]
        {
            ("period read", () => luis.Client.GetAsync($"{V1}/periods/{periodId}", Ct)),
            ("period close", () => luis.Client.PostAsync($"{V1}/periods/{periodId}/close", null, Ct)),
            ("dashboard of her period", () => luis.Client.GetAsync($"{V1}/dashboard?periodId={periodId}", Ct)),
            ("incomes of her period", () => luis.Client.GetAsync($"{V1}/incomes?periodId={periodId}", Ct)),
            ("category rename", () => luis.Client.PatchAsJsonAsync($"{V1}/categories/{categoryId}", new { name = "Robada" }, Ct)),
            ("category delete", () => luis.Client.DeleteAsync($"{V1}/categories/{categoryId}", Ct)),
            ("income read", () => luis.Client.GetAsync($"{V1}/incomes/{incomeId}", Ct)),
            ("income edit", () => luis.Client.PutAsJsonAsync($"{V1}/incomes/{incomeId}", new { description = "X", value = "1", date = Today, source = "X" }, Ct)),
            ("income receive", () => luis.Client.PostAsJsonAsync($"{V1}/incomes/{incomeId}/receive", new { }, Ct)),
            ("income delete", () => luis.Client.DeleteAsync($"{V1}/incomes/{incomeId}", Ct)),
            ("expense read", () => luis.Client.GetAsync($"{V1}/expenses/{expenseId}", Ct)),
            ("expense delete", () => luis.Client.DeleteAsync($"{V1}/expenses/{expenseId}", Ct)),
            ("expense payment", () => luis.Client.PostAsJsonAsync($"{V1}/expenses/{expenseId}/payments", new { value = "1000", date = Today }, Ct)),
        };

        foreach (var (name, call) in attempts)
        {
            var response = await call();
            // Not found, as if it did not exist: nothing reveals that the resource is there (RNF-SEG-05).
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name}: expected 404, got {response.StatusCode}");
        }

        // An expense can only use one of the user's own categories.
        var foreignCategory = await luis.Client.PostAsJsonAsync($"{V1}/expenses",
            new { name = "X", categoryId, budgeted = "1000", date = Today, type = "Fixed" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, foreignCategory.StatusCode);

        // Luis's own lists show none of Ana's rows.
        var luisCategories = await ReadAsync(await luis.Client.GetAsync($"{V1}/categories?includeArchived=true", Ct));
        Assert.DoesNotContain(luisCategories.EnumerateArray(), c => c.GetProperty("id").GetGuid() == categoryId);
        Assert.Empty((await ReadAsync(await luis.Client.GetAsync($"{V1}/periods", Ct))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await luis.Client.GetAsync($"{V1}/incomes", Ct)).StatusCode); // he has no period today

        // Ana's data and figures are untouched.
        var anaDashboardAfter = await ReadAsync(await ana.Client.GetAsync($"{V1}/dashboard", Ct));
        Assert.Equal(anaDashboardBefore.GetRawText(), anaDashboardAfter.GetRawText());
        Assert.Equal("Privada de Ana", (await ReadAsync(await ana.Client.GetAsync($"{V1}/categories", Ct)))
            .EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == categoryId).GetProperty("name").GetString());
    }

    // --- R15.3: 401 on every private route ---

    [Fact]
    public async Task Every_private_route_answers_401_without_a_token()
    {
        var routes = ApiRoutesOf(_host.Services);
        var privateRoutes = routes.Where(r => !r.Anonymous).ToList();
        Assert.NotEmpty(privateRoutes);

        foreach (var route in privateRoutes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(route.Method), Concrete(route.Template))
            {
                Content = route.Method is "GET" or "DELETE" ? null : JsonContent.Create(new { }),
            };
            var response = await _host.Client.SendAsync(request, Ct);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{route.Method} {route.Template}: expected 401, got {response.StatusCode}");
        }
    }

    [Fact]
    public void Only_the_authentication_routes_are_public()
    {
        var anonymous = ApiRoutesOf(_host.Services).Where(r => r.Anonymous).Select(r => r.Template).ToList();
        Assert.NotEmpty(anonymous);
        Assert.All(anonymous, template => Assert.StartsWith("/api/v1/auth/", template));
    }

    // --- OpenAPI (R14.3) ---

    [Fact]
    public async Task The_openapi_document_describes_every_versioned_endpoint()
    {
        using var document = JsonDocument.Parse(await _host.Client.GetStringAsync("/openapi/v1.json", Ct));
        var paths = document.RootElement.GetProperty("paths");

        foreach (var route in ApiRoutesOf(_host.Services).Where(r => !r.Template.Contains("test-whoami", StringComparison.Ordinal)))
        {
            // OpenAPI drops the route constraints: {id:guid} is {id}.
            var path = System.Text.RegularExpressions.Regex.Replace(route.Template, @"\{(\w+):[^}]+\}", "{$1}");
            Assert.True(paths.TryGetProperty(path, out var item), $"OpenAPI has no path {path}");
            Assert.True(item.TryGetProperty(route.Method.ToLowerInvariant(), out _), $"OpenAPI has no {route.Method} {path}");
        }
    }

    // --- Problem Details (R14.1) ---

    [Fact]
    public async Task Every_error_status_is_problem_details_with_the_correlation_id()
    {
        using var session = await _host.SignInAsync();
        using var unverified = await _host.SignInAsync(verifyEmail: false);
        await session.Client.PostAsJsonAsync($"{V1}/categories", new { name = "Duplicada" }, Ct);
        var future = await ReadAsync(await session.Client.PostAsJsonAsync($"{V1}/periods",
            new { startDate = Today.AddDays(60), endDate = Today.AddDays(70) }, Ct));

        var cases = new (HttpStatusCode Status, Func<Task<HttpResponseMessage>> Call)[]
        {
            (HttpStatusCode.BadRequest, () => session.Client.PostAsJsonAsync($"{V1}/incomes", new { description = "X" }, Ct)),
            (HttpStatusCode.Unauthorized, () => _host.Client.GetAsync($"{V1}/profile", Ct)),
            (HttpStatusCode.Forbidden, () => unverified.Client.GetAsync($"{V1}/categories", Ct)),
            (HttpStatusCode.NotFound, () => session.Client.GetAsync($"{V1}/incomes/{Guid.NewGuid()}", Ct)),
            (HttpStatusCode.Conflict, () => session.Client.PostAsJsonAsync($"{V1}/categories", new { name = "duplicada" }, Ct)),
            (HttpStatusCode.UnprocessableEntity, () => session.Client.PostAsync($"{V1}/periods/{future.GetProperty("id").GetGuid()}/close", null, Ct)),
        };

        foreach (var (status, call) in cases)
        {
            var response = await call();
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await ReadAsync(response);
            Assert.Equal((int)status, body.GetProperty("status").GetInt32());
            var correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
            Assert.Equal(correlationId, body.GetProperty("correlationId").GetString());
        }
    }

    // --- Logs (R14.4) ---

    [Fact]
    public async Task Logs_carry_the_correlation_id_and_never_a_secret()
    {
        var sink = new CapturingLoggerProvider();
        using var host = new ApiTestHost(_database, configure: builder =>
        {
            // Everything, down to Trace, from every category: if a secret were logged anywhere, it would show here.
            builder.UseSetting("Logging:LogLevel:Default", "Trace");
            builder.UseSetting("Logging:LogLevel:Microsoft", "Trace");
            builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace");
            builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Trace");
            builder.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(sink)));
        });

        var email = ApiTestHost.UniqueEmail();
        const string newPassword = "another long password";
        // Passwords, tokens, the signing key, database passwords and the email (personal data) must never be logged.
        var secrets = new List<string> { ApiTestHost.Password, newPassword, ApiFactory.TestSigningKey, "app_pwd", "owner_pwd", email };

        var register = await host.Client.PostAsJsonAsync($"{V1}/auth/register", ApiTestHost.Registration(email), Ct);
        var registerCorrelation = Assert.Single(register.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        var verification = host.Notifier.LatestEmailVerification(email)!.Token;
        secrets.Add(verification);
        await host.Client.PostAsJsonAsync($"{V1}/auth/verify-email", new { token = verification }, Ct);

        var tokens = await ReadAsync(await host.Client.PostAsJsonAsync($"{V1}/auth/login", new { email, password = ApiTestHost.Password }, Ct));
        var accessToken = tokens.GetProperty("accessToken").GetString()!;
        secrets.Add(accessToken);
        secrets.Add(tokens.GetProperty("refreshToken").GetString()!);

        using var client = WithToken(host.CreateClient(), accessToken);
        await client.GetAsync($"{V1}/profile", Ct);
        await host.Client.PostAsJsonAsync($"{V1}/auth/login", new { email, password = "a wrong password value" }, Ct);
        secrets.Add("a wrong password value");

        await host.Client.PostAsJsonAsync($"{V1}/auth/forgot-password", new { email }, Ct);
        var reset = host.Notifier.LatestPasswordReset(email)!.Token;
        secrets.Add(reset);
        await host.Client.PostAsJsonAsync($"{V1}/auth/reset-password", new { token = reset, newPassword }, Ct);

        var entries = sink.Entries.ToList();
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            foreach (var secret in secrets)
            {
                Assert.False(entry.Text.Contains(secret, StringComparison.Ordinal), $"A log entry leaks a secret: {entry.Category}: {entry.Text.Replace(secret, "<<SECRET>>")}");
            }
        }

        // The app's own entry for the registration is tied to that request by its correlation id.
        var issued = Assert.Single(entries, e => e.Text.Contains("Email verification issued", StringComparison.Ordinal));
        Assert.Contains(registerCorrelation, issued.Scopes, StringComparison.Ordinal);
    }

    // --- Helpers ---

    private sealed record ApiRoute(string Method, string Template, bool Anonymous);

    /// <summary>Every controller route under /api/v1, with its method and whether it allows anonymous callers.</summary>
    private static List<ApiRoute> ApiRoutesOf(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/v1", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => new ApiRoute(method, "/" + e.RoutePattern.RawText, e.Metadata.GetMetadata<IAllowAnonymous>() is not null)))
            .Distinct()
            .ToList();

    private static string Concrete(string template) =>
        System.Text.RegularExpressions.Regex.Replace(template, @"\{[^}]+\}", Guid.NewGuid().ToString());

    private sealed record LogEntry(string Category, string Text, string Scopes);

    /// <summary>Keeps every log entry with its message, exception and scopes.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var scopes = new System.Text.StringBuilder();
                owner._scopes.ForEachScope((scope, text) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        foreach (var pair in pairs)
                        {
                            text.Append(pair.Key).Append('=').Append(pair.Value).Append(';');
                        }
                    }
                    else
                    {
                        text.Append(scope).Append(';');
                    }
                }, scopes);

                // The structured values too, not only the rendered message.
                var values = state is IEnumerable<KeyValuePair<string, object?>> fields
                    ? string.Join(";", fields.Select(f => $"{f.Key}={f.Value}"))
                    : string.Empty;
                owner.Entries.Enqueue(new LogEntry(category, $"{formatter(state, exception)}|{values}|{exception}", scopes.ToString()));
            }
        }
    }
}
