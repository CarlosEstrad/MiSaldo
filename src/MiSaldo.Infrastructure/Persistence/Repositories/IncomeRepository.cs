using MiSaldo.Application.Abstractions;
using MiSaldo.Domain.Incomes;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence.Repositories;

public sealed class IncomeRepository : IIncomeRepository
{
    private readonly MiSaldoDbContext _db;

    public IncomeRepository(MiSaldoDbContext db) => _db = db;

    public Task<IncomeTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.IncomeTransactions.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    // An income matters to a period when it is dated in it (planned) or was
    // received in it (affects cash).
    public async Task<IReadOnlyList<IncomeTransaction>> ListForRangeAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
        await _db.IncomeTransactions
            .Where(i => (i.Date >= start && i.Date <= end)
                || (i.ReceivedDate != null && i.ReceivedDate >= start && i.ReceivedDate <= end))
            .OrderBy(i => i.Date)
            .ToListAsync(cancellationToken);

    public void Add(IncomeTransaction income) => _db.IncomeTransactions.Add(income);

    public void Remove(IncomeTransaction income) => _db.IncomeTransactions.Remove(income);
}
