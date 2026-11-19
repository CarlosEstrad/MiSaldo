using MiSaldo.Application.Abstractions;
using MiSaldo.Application.Common;
using MiSaldo.Domain.Categories;
namespace MiSaldo.Application.Categories;

public sealed record CategoryDto(Guid Id, string Name, int SortOrder, bool IsArchived);

public sealed record CreateCategoryCommand(string Name);

/// <summary>A partial change: null leaves the field as it is.</summary>
public sealed record UpdateCategoryCommand(Guid Id, string? Name, int? SortOrder, bool? IsArchived);

/// <summary>What happened on delete: a category with movements is archived instead (RF-CAT-03).</summary>
public enum CategoryRemoval
{
    Deleted,
    Archived,
}

/// <summary>
/// Manages the current user's expense categories (RF-CAT-02 to RF-CAT-04). The
/// repository only sees this user's rows, so another user's category is a 404.
/// </summary>
public sealed class CategoryHandlers
{
    public const int MaxNameLength = 128;
    public const string InvalidNameCode = "category.invalid_name";
    public const string DuplicateNameCode = "category.duplicate_name";
    public const string NotFoundCode = "category.not_found";

    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUser;

    public CategoryHandlers(ICategoryRepository categories, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    {
        _categories = categories;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    private static Error NotFound { get; } = Error.NotFound(NotFoundCode, "The category was not found.");

    private static Error Duplicate { get; } = Error.Conflict(DuplicateNameCode, "A category with this name already exists.");

    public async Task<Result<IReadOnlyList<CategoryDto>>> ListAsync(
        bool includeArchived, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure<IReadOnlyList<CategoryDto>>(CurrentUserErrors.NoUser);
        }

        var categories = await _categories.ListAsync(cancellationToken);
        IReadOnlyList<CategoryDto> list = categories
            .Where(c => includeArchived || !c.IsArchived)
            .Select(ToDto)
            .ToList();
        return Result.Success(list);
    }

    public async Task<Result<CategoryDto>> CreateAsync(CreateCategoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_currentUser.UserId is null)
        {
            return Result.Failure<CategoryDto>(CurrentUserErrors.NoUser);
        }

        if (ValidateName(command.Name) is { } invalid)
        {
            return Result.Failure<CategoryDto>(invalid);
        }

        if (await _categories.NameExistsAsync(command.Name, null, cancellationToken))
        {
            return Result.Failure<CategoryDto>(Duplicate);
        }

        var existing = await _categories.ListAsync(cancellationToken);
        var sortOrder = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1;
        var category = new ExpenseCategory(Guid.NewGuid(), command.Name, sortOrder);
        _categories.Add(category);

        if (await TrySaveAsync(cancellationToken) is { } conflict)
        {
            return Result.Failure<CategoryDto>(conflict);
        }

        return Result.Success(ToDto(category));
    }

    public async Task<Result<CategoryDto>> UpdateAsync(UpdateCategoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_currentUser.UserId is null)
        {
            return Result.Failure<CategoryDto>(CurrentUserErrors.NoUser);
        }

        if (command.Name is not null && ValidateName(command.Name) is { } invalid)
        {
            return Result.Failure<CategoryDto>(invalid);
        }

        var category = await _categories.GetByIdAsync(command.Id, cancellationToken);
        if (category is null)
        {
            return Result.Failure<CategoryDto>(NotFound);
        }

        if (command.Name is not null)
        {
            if (await _categories.NameExistsAsync(command.Name, category.Id, cancellationToken))
            {
                return Result.Failure<CategoryDto>(Duplicate);
            }

            category.Rename(command.Name);
        }

        if (command.SortOrder is { } sortOrder)
        {
            category.Reorder(sortOrder);
        }

        if (command.IsArchived is { } archived)
        {
            if (archived)
            {
                category.Archive();
            }
            else
            {
                category.Unarchive();
            }
        }

        if (await TrySaveAsync(cancellationToken) is { } conflict)
        {
            return Result.Failure<CategoryDto>(conflict);
        }

        return Result.Success(ToDto(category));
    }

    /// <summary>Deletes the category, or archives it when expenses use it, keeping their history (RF-CAT-03).</summary>
    public async Task<Result<CategoryRemoval>> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is null)
        {
            return Result.Failure<CategoryRemoval>(CurrentUserErrors.NoUser);
        }

        var category = await _categories.GetByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return Result.Failure<CategoryRemoval>(NotFound);
        }

        CategoryRemoval outcome;
        if (await _categories.HasMovementsAsync(id, cancellationToken))
        {
            category.Archive();
            outcome = CategoryRemoval.Archived;
        }
        else
        {
            _categories.Remove(category);
            outcome = CategoryRemoval.Deleted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(outcome);
    }

    private static CategoryDto ToDto(ExpenseCategory category) =>
        new(category.Id, category.Name, category.SortOrder, category.IsArchived);

    private static Error? ValidateName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength
            ? Error.Validation(InvalidNameCode, $"The name is required and must have at most {MaxNameLength} characters.")
            : null;

    // Two requests creating the same name at once: the unique index decides, and the loser gets the same 409.
    private async Task<Error?> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DuplicateKeyException)
        {
            return Duplicate;
        }
    }
}
