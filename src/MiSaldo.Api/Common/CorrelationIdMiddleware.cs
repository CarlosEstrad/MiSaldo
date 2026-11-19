namespace MiSaldo.Api.Common;

/// <summary>
/// Gives every request a correlation id: the caller's <c>X-Correlation-ID</c>
/// when it is well formed, otherwise a new one. The id is echoed in the
/// response header, added to error responses, and attached to every log entry
/// of the request as a scope (RNF-MAN-05).
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Longest id accepted from a caller; longer ones are replaced.</summary>
    public const int MaxLength = 64;

    private static readonly object ItemKey = new();

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>The correlation id of the request, or null outside this middleware.</summary>
    public static string? Get(HttpContext context) =>
        context?.Items.TryGetValue(ItemKey, out var value) == true ? value as string : null;

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsWellFormed(incoming) ? incoming : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    // Only plain characters are accepted, so a caller cannot inject anything
    // into the logs or the response headers through this value.
    internal static bool IsWellFormed(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
