using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Incomes;

/// <summary>
/// An income for a period. It starts as planned or received; only a received
/// income, dated by its reception date, counts toward the period's cash
/// (RF-ING-01 to RF-ING-04, RN-F02). The amount must be strictly positive.
/// </summary>
public sealed class IncomeTransaction
{
    // For ORM materialization only: EF Core sets the mapped properties, including
    // the Money complex values, after construction. Application code uses the
    // public constructor, which enforces the invariants.
    private IncomeTransaction()
    {
        Description = string.Empty;
        Source = string.Empty;
    }

    public IncomeTransaction(
        Guid id,
        string description,
        Money value,
        DateOnly date,
        string source,
        IncomeStatus status,
        DateOnly? receivedDate = null)
    {
        if (!value.IsPositive)
        {
            throw new NonPositiveAmountException("Income value", value.Amount);
        }

        if (status == IncomeStatus.Received && receivedDate is null)
        {
            throw new ArgumentException(
                "A received income requires a reception date.", nameof(receivedDate));
        }

        if (status == IncomeStatus.Planned && receivedDate is not null)
        {
            throw new ArgumentException(
                "A planned income cannot have a reception date.", nameof(receivedDate));
        }

        Id = id;
        Description = description;
        Value = value;
        Date = date;
        Source = source;
        Status = status;
        ReceivedDate = receivedDate;
    }

    public Guid Id { get; }

    public string Description { get; private set; }

    public Money Value { get; private set; }

    /// <summary>The expected/registration date of the income.</summary>
    public DateOnly Date { get; private set; }

    public string Source { get; private set; }

    public IncomeStatus Status { get; private set; }

    /// <summary>The date the income was received, set when marked received (RN-F02).</summary>
    public DateOnly? ReceivedDate { get; private set; }

    public bool IsReceived => Status == IncomeStatus.Received;

    /// <summary>
    /// Edits the income (RF-ING-05). Whether it was received, and when, is
    /// changed only through <see cref="MarkReceived"/>.
    /// </summary>
    public void Update(string description, Money value, DateOnly date, string source)
    {
        if (!value.IsPositive)
        {
            throw new NonPositiveAmountException("Income value", value.Amount);
        }

        Description = description;
        Value = value;
        Date = date;
        Source = source;
    }

    /// <summary>
    /// Marks a planned income as received on the given date (RF-ING-03). An
    /// income that is already received is not changed by calling this again
    /// with the same date; marking with a different operation is an edit.
    /// </summary>
    public void MarkReceived(DateOnly receivedDate)
    {
        Status = IncomeStatus.Received;
        ReceivedDate = receivedDate;
    }
}
