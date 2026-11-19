namespace MiSaldo.Application.Abstractions;

/// <summary>
/// Commits the changes of a use case as one atomic unit, so operations that
/// touch several entities succeed or fail together (RNF-DAT-01).
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="action"/> inside a single transaction and commits it
    /// when the action completes without throwing.
    /// </summary>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
