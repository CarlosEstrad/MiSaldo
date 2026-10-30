using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Expenses;

/// <summary>
/// A single payment made against an expense. Payments are an immutable history
/// (RN-M08): a correction is a new adjusting movement, never an edit of an
/// existing payment. The value must be strictly positive.
/// </summary>
public sealed class ExpensePayment
{
    public ExpensePayment(Guid id, Money value, DateOnly date)
    {
        if (!value.IsPositive)
        {
            throw new NonPositiveAmountException("Expense payment value", value.Amount);
        }

        Id = id;
        Value = value;
        Date = date;
    }

    public Guid Id { get; }

    public Money Value { get; }

    /// <summary>The payment date. A payment belongs to the period that contains it (RN-M02).</summary>
    public DateOnly Date { get; }
}
