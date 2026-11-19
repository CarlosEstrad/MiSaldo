using MiSaldo.Application.Abstractions;
using MiSaldo.Domain.Periods;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence.Repositories;

public sealed class PeriodRepository : IPeriodRepository
{
    private readonly MiSaldoDbContext _db;

    public PeriodRepository(MiSaldoDbContext db) => _db = db;

    public Task<FinancialPeriod?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.FinancialPeriods.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<FinancialPeriod>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.FinancialPeriods.OrderBy(p => p.StartDate).ToListAsync(cancellationToken);

    // Two inclusive ranges intersect when each starts on or before the other ends.
    public Task<bool> OverlapsAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
        _db.FinancialPeriods.AnyAsync(p => p.StartDate <= end && p.EndDate >= start, cancellationToken);

    public async Task<IReadOnlyList<FinancialPeriod>> ListOverdueAsync(
        DateOnly today, CancellationToken cancellationToken = default) =>
        await _db.FinancialPeriods
            .Where(p => !p.IsClosed && p.EndDate < today)
            .OrderBy(p => p.StartDate)
            .ToListAsync(cancellationToken);

    public void Add(FinancialPeriod period) => _db.FinancialPeriods.Add(period);
}
