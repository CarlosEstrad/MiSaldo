using MiSaldo.Application.Abstractions;
using MiSaldo.Infrastructure;
using MiSaldo.Infrastructure.Auth;
using MiSaldo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>
/// Verifies the container wiring. Resolving services never opens a connection,
/// so these run without Docker.
/// </summary>
public class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider(TestCurrentUser user)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(user);
        services.AddMiSaldoPersistence("Host=localhost;Database=unused;Username=x;Password=x");
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void Repositories_and_unit_of_work_resolve_within_a_scope()
    {
        using var provider = BuildProvider(new TestCurrentUser(Guid.NewGuid()));
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(sp.GetRequiredService<IUserRepository>());
        Assert.NotNull(sp.GetRequiredService<ICategoryRepository>());
        Assert.NotNull(sp.GetRequiredService<IPeriodRepository>());
        Assert.NotNull(sp.GetRequiredService<IIncomeRepository>());
        Assert.NotNull(sp.GetRequiredService<IExpenseRepository>());
        Assert.NotNull(sp.GetRequiredService<MiSaldoDbContext>());
    }

    [Fact]
    public void The_application_accessor_is_the_same_object_as_the_persistence_current_user()
    {
        var user = new TestCurrentUser(Guid.NewGuid());
        using var provider = BuildProvider(user);
        using var scope = provider.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>();

        Assert.Same(user, accessor);
        Assert.Equal(user.UserId, accessor.UserId);
    }

    [Fact]
    public void A_blank_connection_string_is_rejected()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddMiSaldoPersistence(" "));
    }

    [Fact]
    public void The_auth_repository_resolves_within_a_scope()
    {
        using var provider = BuildProvider(new TestCurrentUser(Guid.NewGuid()));
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuthRepository>());
    }

    [Fact]
    public void Authentication_services_resolve_and_work_together()
    {
        var services = new ServiceCollection();
        services.AddMiSaldoAuthentication(
            new JwtOptions { SigningKey = new string('k', 48) },
            new Argon2Options { MemorySizeKiB = 1024, Iterations = 1, Parallelism = 1 });
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var hasher = provider.GetRequiredService<IPasswordHasher>();
        var tokens = provider.GetRequiredService<ITokenService>();

        Assert.True(hasher.Verify(hasher.Hash("wired up password"), "wired up password"));
        Assert.False(string.IsNullOrEmpty(tokens.CreateAccessToken(Guid.NewGuid(), true).Value));
        Assert.IsType<SystemClock>(provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void A_weak_signing_key_stops_the_host_at_registration()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() =>
            services.AddMiSaldoAuthentication(new JwtOptions { SigningKey = "too-short" }));
    }
}
