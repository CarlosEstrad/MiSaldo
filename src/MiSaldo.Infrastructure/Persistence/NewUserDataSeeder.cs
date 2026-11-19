using MiSaldo.Application.Abstractions;
using MiSaldo.Domain.Categories;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence;

/// <summary>
/// Writes the starting data of a brand-new account. Registration has no
/// authenticated user, so the Row Level Security session variable is pointed at
/// the new user with <c>set_config(..., true)</c>, which only lasts until the
/// transaction ends. The RLS policy still checks every row, so this cannot
/// write data for any other account (RNF-SEG-04).
/// </summary>
public sealed class NewUserDataSeeder : INewUserDataSeeder
{
    private readonly MiSaldoDbContext _db;
    private readonly ICurrentUser _currentUser;

    public NewUserDataSeeder(MiSaldoDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task SeedCategoriesAsync(
        Guid newUserId, IReadOnlyList<ExpenseCategory> categories, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(categories);

        if (newUserId == Guid.Empty)
        {
            throw new ArgumentException("The new user id is required.", nameof(newUserId));
        }

        if (_currentUser.UserId is not null)
        {
            throw new InvalidOperationException("New-account data can only be seeded without an authenticated user.");
        }

        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("New-account data must be seeded inside the registration transaction.");
        }

        var userIdText = newUserId.ToString();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.user_id', {userIdText}, true)", cancellationToken);

        foreach (var category in categories)
        {
            _db.ExpenseCategories.Add(category);
            _db.Entry(category).Property("UserId").CurrentValue = newUserId;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
