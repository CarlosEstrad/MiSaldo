using MiSaldo.Domain.Categories;

namespace MiSaldo.Application.Abstractions;

/// <summary>
/// Access to the current user's expense categories. Results are already limited
/// to that user by the persistence layer, so another user's category is simply
/// not found (RNF-SEG-05).
/// </summary>
public interface ICategoryRepository
{
    Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpenseCategory>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the user already has a category with this name, ignoring case (RF-CAT-04).</summary>
    Task<bool> NameExistsAsync(string name, Guid? excludingId, CancellationToken cancellationToken = default);

    /// <summary>True when any expense uses the category, so it must be archived, not deleted (RF-CAT-03).</summary>
    Task<bool> HasMovementsAsync(Guid categoryId, CancellationToken cancellationToken = default);

    void Add(ExpenseCategory category);

    void Remove(ExpenseCategory category);
}
