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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MiSaldoDbContext).Assembly);
    }
}
