using System.Net;
using System.Net.Http.Json;
using MiSaldo.Application.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiSaldo.Api.Tests;

/// <summary>
/// Spec 04, task 5 (🐳): <c>POST /auth/resend-verification</c> over HTTP against
/// a real PostgreSQL. Needs Docker running.
/// </summary>
[Collection(nameof(ApiDatabaseCollection))]
public sealed class ResendVerificationEndpointTests : IDisposable
{
    private const string AuthBase = $"/{ApiRoutes.V1}/auth";

    private readonly ApiPostgresFixture _database;
    private readonly ShiftableClock _clock = new();
    private readonly ApiTestHost _host;

    public ResendVerificationEndpointTests(ApiPostgresFixture database)
    {
        _database = database;
        _host = new ApiTestHost(database, configure: builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(_clock)));
    }

    public void Dispose() => _host.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> PostAsync(string path, object body) =>
        _host.Client.PostAsJsonAsync($"{AuthBase}/{path}", body, Ct);

    private async Task<string> RegisterAsync()
    {
        var email = ApiTestHost.UniqueEmail();
        Assert.Equal(HttpStatusCode.Created, (await PostAsync("register", ApiTestHost.Registration(email))).StatusCode);
        return email;
    }

    private string LatestToken(string email) => _host.Notifier.LatestEmailVerification(email)!.Token;

    [Fact]
    public async Task After_a_resend_the_old_link_fails_and_the_new_one_verifies()
    {
        var email = await RegisterAsync();
        var oldToken = LatestToken(email);

        _clock.Advance(TimeSpan.FromSeconds(61));
        var resend = await PostAsync("resend-verification", new { email });
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);

        var newToken = LatestToken(email);
        Assert.NotEqual(oldToken, newToken);

        var old = await PostAsync("verify-email", new { token = oldToken });
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        Assert.Equal("verification.invalid_token", (await ApiTestHost.ReadAsync(old)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync("verify-email", new { token = newToken })).StatusCode);

        var login = await ApiTestHost.ReadAsync(
            await PostAsync("login", new { email, password = ApiTestHost.Password }));
        Assert.True(login.GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task The_answer_is_the_same_for_unknown_verified_waiting_and_resent_accounts()
    {
        // Waiting: registered less than a minute ago.
        var waiting = await RegisterAsync();
        var waitingToken = LatestToken(waiting);

        // Verified.
        var verified = await RegisterAsync();
        Assert.Equal(
            HttpStatusCode.NoContent, (await PostAsync("verify-email", new { token = LatestToken(verified) })).StatusCode);

        var responses = new List<HttpResponseMessage>
        {
            await PostAsync("resend-verification", new { email = ApiTestHost.UniqueEmail() }),
            await PostAsync("resend-verification", new { email = verified }),
            await PostAsync("resend-verification", new { email = waiting }),
        };

        // Resent: an unverified account past the wait.
        var resent = await RegisterAsync();
        _clock.Advance(TimeSpan.FromSeconds(61));
        var resentBefore = LatestToken(resent);
        responses.Add(await PostAsync("resend-verification", new { email = resent }));
        Assert.NotEqual(resentBefore, LatestToken(resent));

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Ct));
            Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        }

        // The waiting account kept its link: nothing new was issued.
        Assert.Equal(waitingToken, LatestToken(waiting));
    }

    [Fact]
    public async Task A_body_without_an_email_is_rejected_before_the_use_case_runs()
    {
        var response = await PostAsync("resend-verification", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request.invalid", (await ApiTestHost.ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_endpoint_is_under_the_strict_auth_rate_limit()
    {
        using var strict = new ApiTestHost(_database, configure: builder =>
            builder.UseSetting("RateLimiting:AuthPermitLimit", "2"));
        var email = ApiTestHost.UniqueEmail();

        var first = await strict.Client.PostAsJsonAsync($"{AuthBase}/resend-verification", new { email }, Ct);
        var second = await strict.Client.PostAsJsonAsync($"{AuthBase}/resend-verification", new { email }, Ct);
        var third = await strict.Client.PostAsJsonAsync($"{AuthBase}/resend-verification", new { email }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("rate_limit.exceeded", (await ApiTestHost.ReadAsync(third)).GetProperty("code").GetString());
    }
}
