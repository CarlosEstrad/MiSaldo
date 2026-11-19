using MiSaldo.Application.Abstractions;
using MiSaldo.Domain.Categories;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence.Repositories;

public sealed class CategoryRepository : ICategoryRepository
{
    private readonly MiSaldoDbContext _db;

    public CategoryRepository(MiSaldoDbContext db) => _db = db;

    public Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ExpenseCategory>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.ExpenseCategories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(
        string name, Guid? excludingId, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim().ToLowerInvariant();
        return _db.ExpenseCategories.AnyAsync(
            c => c.Name.ToLower() == normalized && (excludingId == null || c.Id != excludingId),
            cancellationToken);
    }

    public Task<bool> HasMovementsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        _db.Expenses.AnyAsync(e => e.CategoryId == categoryId, cancellationToken);

    public void Add(ExpenseCategory category) => _db.ExpenseCategories.Add(category);

    public void Remove(ExpenseCategory category) => _db.ExpenseCategories.Remove(category);
}
