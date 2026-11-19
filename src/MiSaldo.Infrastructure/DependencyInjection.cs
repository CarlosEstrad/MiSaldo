using MiSaldo.Application.Abstractions;
using MiSaldo.Infrastructure.Persistence;
using MiSaldo.Infrastructure.Persistence.Interceptors;
using MiSaldo.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
