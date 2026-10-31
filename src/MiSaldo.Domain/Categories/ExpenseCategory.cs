namespace MiSaldo.Domain.Categories;

/// <summary>
/// A user-owned expense category. Names are unique per user (case-insensitive,
/// RF-CAT-04). A category with movements is never deleted; it is archived to
/// keep history (RF-CAT-03).
/// </summary>
public sealed class ExpenseCategory
{
    public ExpenseCategory(Guid id, string name, int sortOrder = 0)
    {
        Rename(name);
        Id = id;
        SortOrder = sortOrder;
    }

    public Guid Id { get; }

    public string Name { get; private set; } = string.Empty;

    public int SortOrder { get; private set; }

    public bool IsArchived { get; private set; }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        Name = name.Trim();
    }

    public void Reorder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>Archives the category, preserving its history (RF-CAT-03).</summary>
    public void Archive() => IsArchived = true;

    public void Unarchive() => IsArchived = false;
}
