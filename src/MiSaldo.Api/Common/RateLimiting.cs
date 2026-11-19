using System.Globalization;
using System.Threading.RateLimiting;
using MiSaldo.Infrastructure.Auth;
using Microsoft.AspNetCore.RateLimiting;
namespace MiSaldo.Api.Common;

/// <summary>Request limits per minute (RNF-SEG-07). Bound from the <c>RateLimiting</c> section.</summary>
public sealed class RateLimitingOptions
{
    /// <summary>Requests per minute for each user (or address, when anonymous) on regular routes.</summary>
    public int GeneralPermitLimit { get; set; } = 120;

    /// <summary>Requests per minute for each address on <c>/auth/*</c>: stricter against credential guessing.</summary>
    public int AuthPermitLimit { get; set; } = 10;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Rate limiting: a strict partition per client address on the authentication routes, a looser one elsewhere.</summary>
public static class RateLimiting
{
    /// <summary>Path prefix of the authentication endpoints.</summary>
    public static readonly PathString AuthPath = new($"/{ApiRoutes.V1}/auth");

    public static IServiceCollection AddMiSaldoRateLimiting(this IServiceCollection services, RateLimitingOptions limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments(AuthPath, StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"auth:{ClientAddress(context)}", _ => Window(limits.AuthPermitLimit, limits.Window));
                }
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"general:{ClientKey(context)}", _ => Window(limits.GeneralPermitLimit, limits.Window));
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
                }
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Try again later.",
                        Extensions = { ["code"] = "rate_limit.exceeded" },
                    },
                });
            };
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permitLimit, TimeSpan window) => new()
    {
        PermitLimit = permitLimit,
        Window = window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>The user from the validated token when there is one, the address otherwise.</summary>
    private static string ClientKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true && context.User.FindFirst(JwtClaims.Subject)?.Value is { } sub
            ? $"user:{sub}"
            : $"ip:{ClientAddress(context)}";
}
