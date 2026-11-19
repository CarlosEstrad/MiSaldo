using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MiSaldo.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace MiSaldo.Api.Tests;

/// <summary>A private endpoint that only exists in the tests: it echoes the user the API resolved.</summary>
[ApiController]
[Route($"{ApiRoutes.V1}/test-whoami")]
public sealed class WhoAmITestController(ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { userId = currentUser.UserId });
}

/// <summary>
/// Task 7: private routes need a valid token (401 otherwise), the current user
/// comes from the token's <c>sub</c> claim, and rate limiting is stricter on
/// <c>/auth/*</c>. No database is needed: the test endpoint never touches it.
/// </summary>
public class AuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public AuthorizationTests(ApiFactory factory) => _factory = factory;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string WhoAmIPath = $"/{ApiRoutes.V1}/test-whoami";

    private WebApplicationFactory<Program> WithTestEndpoint(Action<IWebHostBuilder>? configure = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => services
                .AddControllers()
                .AddApplicationPart(typeof(WhoAmITestController).Assembly));
            configure?.Invoke(builder);
        });

    [Fact]
    public async Task A_private_route_without_a_token_answers_401()
    {
        using var factory = WithTestEndpoint();
        using var client = factory.CreateClient();
        var response = await client.GetAsync(WhoAmIPath, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_private_route_with_a_tampered_token_answers_401()
    {
        using var factory = WithTestEndpoint();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");
        var response = await client.GetAsync(WhoAmIPath, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_private_route_with_a_valid_token_resolves_the_user_from_sub()
    {
        using var factory = WithTestEndpoint();
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var token = factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(userId, true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        var response = await client.GetAsync(WhoAmIPath, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(userId, body.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task The_user_id_cannot_be_supplied_through_the_query_string()
    {
        using var factory = WithTestEndpoint();
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"{WhoAmIPath}?userId={Guid.NewGuid()}", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_and_openapi_stay_public()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_auth_routes_are_limited_more_strictly_and_answer_429_with_problem_details()
    {
        using var factory = WithTestEndpoint(builder =>
        {
            builder.UseSetting("RateLimiting:AuthPermitLimit", "2");
            builder.UseSetting("RateLimiting:GeneralPermitLimit", "100");
        });
        using var client = factory.CreateClient();
        var authPath = $"/{ApiRoutes.V1}/auth/login";

        // The auth endpoints arrive in task 8: until then these answer 404, still counted by the limiter.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsync(authPath, null, Ct)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsync(authPath, null, Ct)).StatusCode);
        var limited = await client.PostAsync(authPath, null, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await limited.Content.ReadAsStringAsync(Ct));
        Assert.Equal("rate_limit.exceeded", body.RootElement.GetProperty("code").GetString());

        // Other routes keep their own, larger budget.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync(WhoAmIPath, Ct)).StatusCode);
    }
}
