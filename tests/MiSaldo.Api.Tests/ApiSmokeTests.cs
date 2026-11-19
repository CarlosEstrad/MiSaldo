using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MiSaldo.Api.Auth;
using MiSaldo.Api.Common;
using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Accounts;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Common;
using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace MiSaldo.Api.Tests;

/// <summary>
/// Task 6: the API host starts, serves OpenAPI under development, answers
/// errors as Problem Details with a correlation id, and validates the tokens it
/// issues. No database is needed.
/// </summary>
public class ApiSmokeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApiSmokeTests(ApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_host_starts_and_the_health_check_answers()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(Ct));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task The_openapi_document_is_served_in_development()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task An_unknown_route_answers_with_problem_details_and_a_correlation_id()
    {
        using var client = _factory.CreateClient();
        // Anonymous callers get 401 on any route (task 7); an authenticated one gets the real 404.
        var token = _factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(Guid.NewGuid(), true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.Value);

        var response = await client.GetAsync($"/{ApiRoutes.V1}/does-not-exist", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(correlationId, body.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task A_well_formed_correlation_id_from_the_caller_is_kept()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "abc-123_X.y");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal("abc-123_X.y", Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));
    }

    [Theory]
    [InlineData("has spaces in it")]
    [InlineData("semi;colon")]
    [InlineData("<script>")]
    public async Task A_malformed_correlation_id_is_replaced(string incoming)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, incoming);

        var response = await client.SendAsync(request, Ct);

        var echoed = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.NotEqual(incoming, echoed);
        Assert.Equal(32, echoed.Length);
    }

    [Fact]
    public void A_correlation_id_longer_than_the_limit_is_rejected()
    {
        Assert.True(CorrelationIdMiddleware.IsWellFormed(new string('a', CorrelationIdMiddleware.MaxLength)));
        Assert.False(CorrelationIdMiddleware.IsWellFormed(new string('a', CorrelationIdMiddleware.MaxLength + 1)));
    }

    [Fact]
    public async Task The_bearer_scheme_accepts_our_tokens_and_rejects_foreign_ones()
    {
        var parameters = _factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;
        var handler = new JsonWebTokenHandler();
        var userId = Guid.NewGuid();

        var ours = _factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(userId, true);
        var valid = await handler.ValidateTokenAsync(ours.Value, parameters);
        Assert.True(valid.IsValid, valid.Exception?.Message);
        Assert.Equal(userId.ToString(), valid.ClaimsIdentity.Name);

        var foreignService = new JwtTokenService(
            new JwtOptions { SigningKey = new string('z', 48) }, new SystemClock());
        var foreign = await handler.ValidateTokenAsync(
            foreignService.CreateAccessToken(userId, true).Value, parameters);
        Assert.False(foreign.IsValid);

        var otherAudience = new JwtTokenService(
            new JwtOptions { SigningKey = ApiFactory.TestSigningKey, Audience = "someone-else" }, new SystemClock());
        Assert.False((await handler.ValidateTokenAsync(
            otherAudience.CreateAccessToken(userId, true).Value, parameters)).IsValid);
    }

    [Fact]
    public void The_use_cases_and_the_current_user_resolve_from_the_container()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<RegisterUserHandler>());
        Assert.NotNull(services.GetRequiredService<VerifyEmailHandler>());
        Assert.NotNull(services.GetRequiredService<LoginHandler>());
        Assert.NotNull(services.GetRequiredService<RefreshSessionHandler>());
        Assert.NotNull(services.GetRequiredService<LogoutHandler>());
        Assert.NotNull(services.GetRequiredService<ForgotPasswordHandler>());
        Assert.NotNull(services.GetRequiredService<ResetPasswordHandler>());

        Assert.IsType<HttpCurrentUser>(services.GetRequiredService<ICurrentUser>());
        Assert.Same(services.GetRequiredService<ICurrentUser>(), services.GetRequiredService<ICurrentUserAccessor>());
        Assert.IsType<InMemoryAccountNotifier>(services.GetRequiredService<IAccountNotifier>());
    }

    [Fact]
    public void The_host_does_not_start_without_a_signing_key()
    {
        using var factory = ApiFactory.With(signingKey: null, connectionString: "Host=localhost;Database=x");

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("signing key", Flatten(error), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_host_does_not_start_without_a_connection_string()
    {
        using var factory = ApiFactory.With(ApiFactory.TestSigningKey, connectionString: null);

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("connection string", Flatten(error), StringComparison.OrdinalIgnoreCase);
    }

    private static string Flatten(Exception error)
    {
        var text = new StringBuilder();
        for (var current = error; current is not null; current = current.InnerException)
        {
            text.AppendLine(current.Message);
        }

        return text.ToString();
    }
}

/// <summary>The current user is read only from the token's <c>sub</c> claim.</summary>
public class HttpCurrentUserTests
{
    private static HttpCurrentUser For(ClaimsPrincipal principal) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });

    [Fact]
    public void An_authenticated_token_gives_the_user_id()
    {
        var id = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtClaims.Subject, id.ToString())], "Bearer"));

        Assert.Equal(id, For(principal).UserId);
    }

    [Fact]
    public void An_anonymous_request_has_no_user_even_with_a_sub_claim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtClaims.Subject, Guid.NewGuid().ToString())]));

        Assert.Null(For(principal).UserId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void A_malformed_sub_gives_no_user(string sub)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtClaims.Subject, sub)], "Bearer"));

        Assert.Null(For(principal).UserId);
    }

    [Fact]
    public void Outside_a_request_there_is_no_user()
    {
        Assert.Null(new HttpCurrentUser(new HttpContextAccessor()).UserId);
    }
}

/// <summary>Exceptions and use-case errors map to the statuses of the design.</summary>
public class ErrorMappingTests
{
    private sealed class SampleRuleException() : DomainException("The amount must be positive.");

    [Fact]
    public async Task A_domain_exception_becomes_a_422_problem_with_its_neutral_message()
    {
        var (status, body) = await HandleAsync(new SampleRuleException());

        Assert.Equal(422, status);
        Assert.Equal("The amount must be positive.", body.GetProperty("detail").GetString());
        Assert.Equal("domain.sample_rule", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_duplicate_key_becomes_a_409_without_the_constraint_name()
    {
        var (status, body) = await HandleAsync(
            new DuplicateKeyException("IX_user_profiles_Email_lower", new InvalidOperationException()));

        Assert.Equal(409, status);
        Assert.DoesNotContain("IX_user_profiles", body.GetRawText());
        Assert.Equal("conflict.duplicate", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_concurrency_conflict_becomes_a_409()
    {
        var (status, body) = await HandleAsync(new ConcurrencyConflictException(new InvalidOperationException()));

        Assert.Equal(409, status);
        Assert.Equal("conflict.concurrency", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unexpected_error_becomes_a_generic_500_that_hides_the_message()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("password=hunter2 at Host=db"));

        Assert.Equal(500, status);
        Assert.DoesNotContain("hunter2", body.GetRawText());
        Assert.Equal("server.error", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unprocessable, 422)]
    public void Each_error_type_has_its_status(ErrorType type, int status)
    {
        Assert.Equal(status, ErrorStatus.For(type));
    }

    [Theory]
    [InlineData("NonPositiveAmountException", "non_positive_amount")]
    [InlineData("UnderageUserException", "underage_user")]
    [InlineData("Plain", "plain")]
    public void Exception_names_become_snake_case_codes(string typeName, string code)
    {
        Assert.Equal(code, ErrorCodes.FromTypeName(typeName));
    }

    [Fact]
    public async Task An_unexpected_error_is_logged_with_its_exception_but_not_sent_to_the_caller()
    {
        var logger = new ListLogger();
        var failure = new InvalidOperationException("boom in the repository");

        var (status, body) = await HandleAsync(failure, logger);

        Assert.Equal(500, status);
        Assert.DoesNotContain("boom", body.GetRawText());
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
    }

    [Fact]
    public async Task Expected_rejections_are_not_logged_as_errors()
    {
        var logger = new ListLogger();
        await HandleAsync(new SampleRuleException(), logger);
        await HandleAsync(new DuplicateKeyException("IX_user_profiles_Email_lower", new InvalidOperationException()), logger);
        await HandleAsync(new ConcurrencyConflictException(new InvalidOperationException()), logger);

        Assert.Equal(3, logger.Entries.Count);
        Assert.All(logger.Entries, e => Assert.True(e.Level < LogLevel.Error, e.Level.ToString()));
        // The constraint name, hidden from the caller, is kept for diagnosis.
        Assert.Contains(logger.Entries, e => e.Message.Contains("IX_user_profiles_Email_lower", StringComparison.Ordinal));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class ListLogger : ILogger<ApiExceptionHandler>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private static async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception, ILogger<ApiExceptionHandler>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();
        var handler = new ApiExceptionHandler(
            provider.GetRequiredService<IProblemDetailsService>(), logger ?? NullLogger<ApiExceptionHandler>.Instance);

        Assert.True(await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken));

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }
}
