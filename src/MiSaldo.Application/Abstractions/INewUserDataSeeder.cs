using MiSaldo.Domain.Categories;

namespace MiSaldo.Application.Abstractions;

/// <summary>
/// Creates the starting data of an account that was just registered, such as
/// the initial expense categories (RF-CAT-01). Registration runs without an
/// authenticated user, so this is the one place allowed to write per-user data
/// on behalf of the new account. It must run inside the registration
/// transaction.
/// </summary>
public interface INewUserDataSeeder
{
    Task SeedCategoriesAsync(
        Guid newUserId, IReadOnlyList<ExpenseCategory> categories, CancellationToken cancellationToken = default);
}
