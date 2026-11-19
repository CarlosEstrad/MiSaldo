using MiSaldo.Domain.Accounts;

namespace MiSaldo.Application.Abstractions;

/// <summary>
/// Access to account data: users, profiles, settings and consent. Account
/// tables are keyed by the user and are not covered by the per-user query
/// filter, so lookups here are always by an explicit id or email.
/// </summary>
public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    Task<UserProfile?> GetProfileByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserSettings?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken = default);

    void Add(User user);

    void Add(UserProfile profile);

    void Add(UserSettings settings);

    void Add(ConsentRecord consent);
}
