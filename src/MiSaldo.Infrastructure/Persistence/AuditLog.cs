namespace MiSaldo.Infrastructure.Persistence;

/// <summary>
/// An audit record of a change to a financial movement: who, when, and the
/// before/after values (RF-HIS-04). This is a persistence concern, so it lives
/// in infrastructure rather than the domain. Written by the auditing
/// interceptor, never edited.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    /// <summary>The entity type that changed (e.g. "Expense").</summary>
    public required string EntityType { get; init; }

    public Guid EntityId { get; init; }

    /// <summary>The kind of change: Created, Modified or Deleted.</summary>
    public required string Action { get; init; }

    /// <summary>Serialized previous values, null for a creation.</summary>
    public string? OldValues { get; init; }

    /// <summary>Serialized new values, null for a deletion.</summary>
    public string? NewValues { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}
