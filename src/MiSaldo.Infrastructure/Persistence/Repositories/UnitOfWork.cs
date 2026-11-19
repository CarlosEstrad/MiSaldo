using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MiSaldo.Infrastructure.Persistence.Repositories;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly MiSaldoDbContext _db;

    public UnitOfWork(MiSaldoDbContext db) => _db = db;

    // EF Core exceptions are translated into application exceptions, so use
    // cases can react to a duplicate or a concurrent change without depending
    // on EF Core or the database driver.
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            throw new DuplicateKeyException(unique.ConstraintName, ex);
        }
    }

    // Everything the action saves is committed together, or rolled back if the
    // action throws (RNF-DAT-01). The action decides when to call SaveChanges.
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
