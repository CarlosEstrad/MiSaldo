using System.Text.Json;
using MiSaldo.Api.Auth;
using Xunit;

namespace MiSaldo.Api.Tests;

/// <summary>
/// Spec 04, task 10: the OpenAPI document describes the resend endpoint and
/// says that the refresh token travels in a cookie (R9.3). No database needed.
/// </summary>
public sealed class OpenApiDocumentationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OpenApiDocumentationTests(ApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonElement> OperationAsync(string path)
    {
        using var client = _factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", Ct));
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("post").Clone();
    }

    [Fact]
    public async Task The_resend_endpoint_is_documented_with_its_uniform_202()
    {
        var operation = await OperationAsync("/api/v1/auth/resend-verification");

        Assert.True(operation.GetProperty("responses").TryGetProperty("202", out _));
        Assert.True(operation.TryGetProperty("requestBody", out _));
        Assert.Contains("always 202", operation.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/logout")]
    [InlineData("/api/v1/auth/reset-password")]
    public async Task The_session_endpoints_say_the_refresh_token_is_in_the_cookie(string path)
    {
        var operation = await OperationAsync(path);

        Assert.Contains(RefreshCookie.Name, operation.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
    }

    [Theory]
    [InlineData("/api/v1/auth/refresh")]
    [InlineData("/api/v1/auth/logout")]
    public async Task Refresh_and_logout_take_no_body_and_document_the_origin_check(string path)
    {
        var operation = await OperationAsync(path);

        Assert.False(operation.TryGetProperty("requestBody", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _));
    }

    [Fact]
    public async Task The_session_response_schema_has_no_refresh_token()
    {
        using var client = _factory.CreateClient();
        var raw = await client.GetStringAsync("/openapi/v1.json", Ct);
        using var document = JsonDocument.Parse(raw);

        var schema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("SessionResponse");
        var properties = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("accessToken", properties);
        Assert.Contains("sessionExpiresAt", properties);
        Assert.DoesNotContain(properties, p => p.Contains("refresh", StringComparison.OrdinalIgnoreCase));
    }
}
