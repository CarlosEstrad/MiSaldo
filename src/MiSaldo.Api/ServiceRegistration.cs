using System.Text.Json.Serialization;
using MiSaldo.Api.Auth;
using MiSaldo.Api.Common;
using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Accounts;
using MiSaldo.Application.Categories;
using MiSaldo.Application.Dashboard;
using MiSaldo.Application.Movements;
using MiSaldo.Application.Periods;
using MiSaldo.Application.Profile;
using MiSaldo.Application.Settings;
using MiSaldo.Infrastructure;
using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace MiSaldo.Api;

/// <summary>Wires the API: configuration, persistence, authentication, use cases and the HTTP pipeline services.</summary>
public static class ServiceRegistration
{
    /// <summary>Configuration key of the connection string (<c>ConnectionStrings__MiSaldo</c>).</summary>
    public const string ConnectionStringName = "MiSaldo";

    /// <summary>Fallback key shared with the EF Core design-time factory and <c>.env.example</c>.</summary>
    public const string ConnectionStringFallbackKey = "MISALDO_DB";

    /// <summary>The signing key as the environment variable <c>JWT__SIGNING_KEY</c> exposes it.</summary>
    public const string SigningKeyEnvironmentKey = "JWT:SIGNING_KEY";

    public static IServiceCollection AddMiSaldoApi(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? configuration[ConnectionStringFallbackKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The database connection string is missing. Set ConnectionStrings__{ConnectionStringName} or {ConnectionStringFallbackKey}.");
        }

        var jwtOptions = ReadJwtOptions(configuration);

        // Email and web-client settings are checked before anything else is wired,
        // so a bad deployment fails at startup, not on the first registration.
        var webClient = WebClientConfiguration.Read(configuration, environment.IsProduction());
        services.AddSingleton(webClient);
        services.AddSingleton(webClient.Smtp);
        services.AddSingleton(webClient.Frontend);
        services.AddSingleton(webClient.Cors);
        var argon2Options = configuration.GetSection("Argon2").Get<Argon2Options>();

        // The current user comes only from the validated access token (RF-AUT-09).
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddMiSaldoPersistence(connectionString);
        services.AddMiSaldoAuthentication(jwtOptions, argon2Options);
        services.AddMiSaldoUseCases(configuration);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep "sub" as "sub" instead of mapping it to a long SOAP claim type.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = JwtValidation.CreateParameters(jwtOptions);
            });
        // Every route needs a valid token unless it opts out with [AllowAnonymous] (RF-AUT-08).
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            AuthPolicies.Add(options);
        });
        services.AddMiSaldoRateLimiting(
            configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions());

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            if (CorrelationIdMiddleware.Get(context.HttpContext) is { } correlationId)
            {
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
            }
        });
        services.AddExceptionHandler<ApiExceptionHandler>();

        services.AddControllers()
            .AddJsonOptions(options =>
                // Enums travel by name ("Monthly", "Dark"); numbers are refused so only known values get in.
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)))
            .ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context =>
            {
                // A malformed body is rejected before any use case runs (RNF-SEG-06), as Problem Details.
                var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
                var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState);
                problem.Extensions["code"] = "request.invalid";
                return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
            });
        services.AddOpenApi();
        services.AddHealthChecks();

        return services;
    }

    private static IServiceCollection AddMiSaldoUseCases(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new RegistrationOptions());
        services.AddSingleton(configuration.GetSection("Authentication").Get<AuthenticationOptions>() ?? new AuthenticationOptions());

        // No real email yet (SES arrives later): tokens stay in memory and are never logged.
        services.AddSingleton<InMemoryAccountNotifier>();
        services.AddSingleton<IAccountNotifier>(provider => provider.GetRequiredService<InMemoryAccountNotifier>());

        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<VerifyEmailHandler>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshSessionHandler>();
        services.AddScoped<LogoutHandler>();
        services.AddScoped<ForgotPasswordHandler>();
        services.AddScoped<ResetPasswordHandler>();

        services.AddScoped<GetProfileHandler>();
        services.AddScoped<UpdateProfileHandler>();
        services.AddScoped<GetSettingsHandler>();
        services.AddScoped<UpdateSettingsHandler>();
        services.AddScoped<CategoryHandlers>();
        services.AddScoped<PeriodHandlers>();
        services.AddScoped<PeriodWriteRules>();
        services.AddScoped<IncomeHandlers>();
        services.AddScoped<ExpenseHandlers>();
        services.AddScoped<GetDashboardHandler>();

        return services;
    }

    private static JwtOptions ReadJwtOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (string.IsNullOrEmpty(options.SigningKey))
        {
            options.SigningKey = configuration[SigningKeyEnvironmentKey] ?? string.Empty;
        }

        return options;
    }
}
