using System.Text.Json;
using MiSaldo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MiSaldo.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every create, modify or delete of a
/// user-scoped entity (RF-HIS-04): who did it, when, and the before/after
/// values. It runs just before the changes are saved, in the same transaction,
/// so the audit trail and the change commit together (RNF-DAT-01).
/// </summary>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;

    public AuditingInterceptor(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            AddAuditEntries(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            AddAuditEntries(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext context)
    {
        var scoped = new HashSet<Type>(MiSaldoDbContext.UserScopedEntityTypes);
        var now = DateTimeOffset.UtcNow;

        // Snapshot the audited entries first; adding AuditLog rows mutates the
        // change tracker, so we must not enumerate it while adding.
        var audited = context.ChangeTracker.Entries()
            .Where(e => scoped.Contains(e.Entity.GetType())
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => new
            {
                e.State,
                // Who made the change: the authenticated user, or, while a new
                // account is being created, the account the row belongs to.
                ActorId = _currentUser.UserId ?? (e.Property("UserId").CurrentValue as Guid?) ?? Guid.Empty,
                EntityType = e.Entity.GetType().Name,
                EntityId = (Guid)(e.Property("Id").CurrentValue ?? Guid.Empty),
                Old = e.State == EntityState.Added ? null : SerializeValues(e, original: true),
                New = e.State == EntityState.Deleted ? null : SerializeValues(e, original: false),
            })
            .ToList();

        foreach (var entry in audited)
        {
            context.Set<AuditLog>().Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = entry.ActorId,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                Action = entry.State.ToString(),
                OldValues = entry.Old,
                NewValues = entry.New,
                OccurredAt = now,
            });
        }
    }

    private static string SerializeValues(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            // Skip the concurrency token and audit-only shadow fields.
            if (property.Metadata.Name is "xmin")
            {
                continue;
            }

            values[property.Metadata.Name] = original
                ? property.OriginalValue
                : property.CurrentValue;
        }

        return JsonSerializer.Serialize(values);
    }
}
