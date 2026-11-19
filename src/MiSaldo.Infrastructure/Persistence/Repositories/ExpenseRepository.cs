using MiSaldo.Application.Abstractions;
using MiSaldo.Domain.Expenses;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence.Repositories;

public sealed class ExpenseRepository : IExpenseRepository
{
    private readonly MiSaldoDbContext _db;

    public ExpenseRepository(MiSaldoDbContext db) => _db = db;

    public Task<Expense?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Expenses.Include(e => e.Payments).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    // An expense matters to a period when it is dated in it (its outstanding
    // obligation belongs there) or any payment falls in it (RN-M02).
    public async Task<IReadOnlyList<Expense>> ListForRangeAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
        await _db.Expenses
            .Include(e => e.Payments)
            .Where(e => (e.Date >= start && e.Date <= end)
                || e.Payments.Any(p => p.Date >= start && p.Date <= end))
            .OrderBy(e => e.Date)
            .ToListAsync(cancellationToken);

    public void Add(Expense expense) => _db.Expenses.Add(expense);

    public void Remove(Expense expense) => _db.Expenses.Remove(expense);
}
