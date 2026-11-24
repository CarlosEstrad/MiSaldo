using MiSaldo.Api.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Net.Http.Headers;

namespace MiSaldo.Api.Auth;

/// <summary>
/// Guards the routes that act on the refresh cookie (refresh, logout) against
/// cross-site requests (R6.1, design E10). When the browser sends an
/// <c>Origin</c>, it must be an allowed CORS origin or the API's own origin;
/// otherwise the request is refused with 403 before the action runs, so the
/// session is not touched. Requests without <c>Origin</c> (clients that are not
/// browsers) are accepted. This is on top of <c>SameSite=Strict</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class SameOriginGuardAttribute : Attribute, IAsyncActionFilter
{
    public const string OriginNotAllowedCode = "auth.origin_not_allowed";

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(HeaderNames.Origin, out var values))
        {
            return next();
        }

        var cors = http.RequestServices.GetRequiredService<CorsSettings>();
        if (values.Count == 1 && IsAllowed(values[0], http.Request, cors))
        {
            return next();
        }

        var factory = http.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(
            http,
            statusCode: StatusCodes.Status403Forbidden,
            detail: "This request comes from a site that is not allowed to use the session.");
        problem.Extensions["code"] = OriginNotAllowedCode;
        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentTypes = { "application/problem+json" },
        };
        return Task.CompletedTask;
    }

    /// <summary>True for an allowed CORS origin or the API's own origin. <c>null</c> and malformed values are refused.</summary>
    internal static bool IsAllowed(string? origin, HttpRequest request, CorsSettings cors)
    {
        var normalized = WebClientConfiguration.NormalizeOrigin(origin);
        if (normalized is null)
        {
            return false;
        }

        if (cors.AllowedOrigins.Contains(normalized, StringComparer.Ordinal))
        {
            return true;
        }

        var own = WebClientConfiguration.NormalizeOrigin($"{request.Scheme}://{request.Host}");
        return own is not null && string.Equals(normalized, own, StringComparison.Ordinal);
    }
}
