using MiSaldo.Application.Abstractions;
using MiSaldo.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace MiSaldo.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly MiSaldoDbContext _db;

    public UserRepository(MiSaldoDbContext db) => _db = db;

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(email);
        return _db.UserProfiles.AnyAsync(p => p.Email.ToLower() == normalized, cancellationToken);
    }

    public Task<UserProfile?> GetProfileByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(email);
        return _db.UserProfiles.FirstOrDefaultAsync(p => p.Email.ToLower() == normalized, cancellationToken);
    }

    public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public Task<UserSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    public void Add(User user) => _db.Users.Add(user);

    public void Add(UserProfile profile) => _db.UserProfiles.Add(profile);

    public void Add(UserSettings settings) => _db.UserSettings.Add(settings);

    public void Add(ConsentRecord consent) => _db.ConsentRecords.Add(consent);

    // Emails are compared case-insensitively.
    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
