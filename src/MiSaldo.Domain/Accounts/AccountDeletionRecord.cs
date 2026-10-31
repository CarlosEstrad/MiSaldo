namespace MiSaldo.Domain.Accounts;

/// <summary>
/// The only trace kept after an account is deleted: the reason and who performed
/// the deletion, plus the timestamp. It holds no personal or financial data
/// (D-12, RF-PRV-03).
/// </summary>
public sealed class AccountDeletionRecord
{
    public AccountDeletionRecord(
        Guid id,
        string reason,
        Guid performedByUserId,
        DateTimeOffset deletedAt)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Deletion reason is required.", nameof(reason));
        }

        Id = id;
        Reason = reason;
        PerformedByUserId = performedByUserId;
        DeletedAt = deletedAt;
    }

    public Guid Id { get; }

    public string Reason { get; }

    /// <summary>Who performed the deletion. Not personal data, just the actor's id.</summary>
    public Guid PerformedByUserId { get; }

    public DateTimeOffset DeletedAt { get; }
}
