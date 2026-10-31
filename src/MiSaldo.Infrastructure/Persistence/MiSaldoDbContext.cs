using System.Linq.Expressions;
using MiSaldo.Domain.Accounts;
using MiSaldo.Domain.Categories;
using MiSaldo.Domain.Expenses;
using MiSaldo.Domain.Incomes;
using MiSaldo.Domain.Periods;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence;

/// <summary>
/// The EF Core context for MiSaldo on PostgreSQL. It maps the F1 domain and
/// account entities. Saldos are never stored; only movements are persisted and
/// the domain engine computes balances (RNF-DAT-07). The global per-user query
/// filter and the UserId stamping are added in a later task; this context sets
/// up the model and the entity configurations.
/// </summary>
public sealed class MiSaldoDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    public MiSaldoDbContext(DbContextOptions<MiSaldoDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // Account entities
    public DbSet<User> Users => Set<User>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    public DbSet<AccountDeletionRecord> AccountDeletionRecords => Set<AccountDeletionRecord>();

    // Financial entities
    public DbSet<FinancialPeriod> FinancialPeriods => Set<FinancialPeriod>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    public DbSet<IncomeTransaction> IncomeTransactions => Set<IncomeTransaction>();

    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<ExpensePayment> ExpensePayments => Set<ExpensePayment>();

    // Infrastructure
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// The entity types that carry a per-user <c>UserId</c> and are isolated by
    /// the global query filter (first isolation barrier, RNF-SEG-04).
    /// </summary>
    internal static readonly Type[] UserScopedEntityTypes =
    [
        typeof(FinancialPeriod),
        typeof(ExpenseCategory),
        typeof(IncomeTransaction),
        typeof(Expense),
        typeof(ExpensePayment),
    ];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiSaldoDbContext).Assembly);

        foreach (var entityType in UserScopedEntityTypes)
        {
            ApplyUserFilterMethod
                .MakeGenericMethod(entityType)
                .Invoke(this, [modelBuilder]);
        }
    }

    private static readonly System.Reflection.MethodInfo ApplyUserFilterMethod =
        typeof(MiSaldoDbContext).GetMethod(
            nameof(ApplyUserFilter),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    // Applies a global query filter so every query on a user-scoped entity only
    // sees rows whose shadow UserId equals the current user's id (RNF-SEG-04).
    private void ApplyUserFilter<T>(ModelBuilder modelBuilder)
        where T : class
    {
        Expression<Func<T, bool>> filter = e =>
            EF.Property<Guid>(e, "UserId") == _currentUser.UserId;
        modelBuilder.Entity<T>().HasQueryFilter(filter);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUserScopedEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampUserScopedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Stamps UserId (on insert) and CreatedAt/UpdatedAt on user-scoped entities.
    // Fails safe: a write with no user in context is rejected (RNF-SEG-04).
    private void StampUserScopedEntities()
    {
        var scoped = new HashSet<Type>(UserScopedEntityTypes);
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (!scoped.Contains(entry.Entity.GetType()))
            {
                continue;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                var userId = _currentUser.UserId
                    ?? throw new InvalidOperationException(
                        "Cannot persist a user-scoped entity without a current user.");

                if (entry.State == EntityState.Added)
                {
                    entry.Property("UserId").CurrentValue = userId;
                    entry.Property("CreatedAt").CurrentValue = now;
                }

                entry.Property("UpdatedAt").CurrentValue = now;
            }
        }
    }
}
