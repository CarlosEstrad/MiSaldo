using MiSaldo.Application.Abstractions;
using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Persistence;
using MiSaldo.Infrastructure.Persistence.Interceptors;
using MiSaldo.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MiSaldo.Infrastructure;

/// <summary>Registers the persistence layer in the dependency injection container.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds the DbContext (with the RLS and auditing interceptors), the
    /// repositories and the unit of work. The host must register an
    /// <see cref="ICurrentUser"/>; it is resolved per request so the data
    /// isolation always uses the authenticated user (RNF-SEG-04).
    /// </summary>
    public static IServiceCollection AddMiSaldoPersistence(
        this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MiSaldoDbContext>((provider, options) =>
        {
            var currentUser = provider.GetRequiredService<ICurrentUser>();
            options
                .UseNpgsql(connectionString)
                .AddInterceptors(
                    new RlsConnectionInterceptor(currentUser),
                    new AuditingInterceptor(currentUser));
        });

        // One object serves both the use cases and the data isolation.
        services.AddScoped<ICurrentUserAccessor>(provider => provider.GetRequiredService<ICurrentUser>());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IPeriodRepository, PeriodRepository>();
        services.AddScoped<IIncomeRepository, IncomeRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<INewUserDataSeeder, NewUserDataSeeder>();

        return services;
    }

    /// <summary>
    /// Adds the backend's own authentication services: the Argon2id password
    /// hasher, the JWT token service and the system clock. The options are
    /// validated here, so a weak signing key or a bad lifetime stops the host at
    /// startup instead of surfacing at the first login.
    /// </summary>
    public static IServiceCollection AddMiSaldoAuthentication(
        this IServiceCollection services, JwtOptions jwtOptions, Argon2Options? argon2Options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(jwtOptions);

        jwtOptions.Validate();
        var argon2 = argon2Options ?? new Argon2Options();
        argon2.Validate();

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton(jwtOptions);
        services.AddSingleton<IPasswordHasher>(new PasswordHasher(argon2));
        services.AddSingleton<ITokenService>(provider =>
            new JwtTokenService(jwtOptions, provider.GetRequiredService<IClock>()));

        return services;
    }
}
