using MiSaldo.Domain.Periods;

namespace MiSaldo.Application.Abstractions;

/// <summary>Access to the current user's financial periods.</summary>
public interface IPeriodRepository
{
    Task<FinancialPeriod?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinancialPeriod>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>True when an existing period of the user intersects the given range (RF-PRD-03).</summary>
    Task<bool> OverlapsAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);

    /// <summary>Periods that ended before <paramref name="today"/> and are still open (RF-PRD-06b).</summary>
    Task<IReadOnlyList<FinancialPeriod>> ListOverdueAsync(DateOnly today, CancellationToken cancellationToken = default);

    void Add(FinancialPeriod period);
}
