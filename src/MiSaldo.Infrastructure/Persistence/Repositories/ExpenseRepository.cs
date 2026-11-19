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

    public Task<bool> HasAnyAsync(CancellationToken cancellationToken = default) =>
        _db.Expenses.AnyAsync(cancellationToken);

    public async Task<StoredPayment?> FindPaymentByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        // The per-user filter applies, so a key is only ever matched within the current user's payments.
        var match = await _db.ExpensePayments
            .Where(p => EF.Property<string?>(p, "IdempotencyKey") == idempotencyKey)
            .Select(p => new { ExpenseId = EF.Property<Guid>(p, "ExpenseId"), Payment = p })
            .FirstOrDefaultAsync(cancellationToken);
        return match is null ? null : new StoredPayment(match.ExpenseId, match.Payment);
    }

    public void AddPayment(Expense expense, ExpensePayment payment, string? idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(expense);
        ArgumentNullException.ThrowIfNull(payment);
        expense.AddPayment(payment);

        // The foreign key and the idempotency key are shadow properties: the domain does not know them.
        var entry = _db.Entry(payment);
        entry.State = EntityState.Added;
        entry.Property("ExpenseId").CurrentValue = expense.Id;
        entry.Property("IdempotencyKey").CurrentValue = idempotencyKey;
    }

    public void Add(Expense expense) => _db.Expenses.Add(expense);

    public void Remove(Expense expense) => _db.Expenses.Remove(expense);
}
