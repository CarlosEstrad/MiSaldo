using MiSaldo.Api.Movements;
using Microsoft.Net.Http.Headers;

namespace MiSaldo.Api.Common;

/// <summary>
/// CORS for the web frontend (R7, design E11): only the configured origins get
/// permission, with credentials so the refresh cookie travels. The origins were
/// already validated at startup (no wildcards, no paths). Without configured
/// origins nobody gets CORS permission: same origin only.
/// </summary>
public static class CorsSetup
{
    public const string PolicyName = "MiSaldoFrontend";

    /// <summary>How long a browser may reuse a preflight answer.</summary>
    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    public static readonly string[] AllowedMethods =
        [HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete];

    public static readonly string[] AllowedHeaders =
    [
        HeaderNames.Authorization,
        HeaderNames.ContentType,
        ExpensesController.IdempotencyKeyHeader,
        CorrelationIdMiddleware.HeaderName,
    ];

    public static readonly string[] ExposedHeaders =
    [
        CorrelationIdMiddleware.HeaderName,
        ExpensesController.ReplayedHeader,
        HeaderNames.RetryAfter,
        HeaderNames.Location,
    ];

    public static IServiceCollection AddMiSaldoCors(this IServiceCollection services, CorsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .WithOrigins([.. settings.AllowedOrigins])
            .AllowCredentials()
            .WithMethods(AllowedMethods)
            .WithHeaders(AllowedHeaders)
            .WithExposedHeaders(ExposedHeaders)
            .SetPreflightMaxAge(PreflightMaxAge)));

        return services;
    }
}
