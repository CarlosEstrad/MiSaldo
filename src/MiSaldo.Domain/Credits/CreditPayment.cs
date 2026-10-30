using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Credits;

/// <summary>
/// A payment made against a credit. Payments are an immutable history
/// (RF-CRD-07, RN-C06): a correction is a new adjusting movement, never an edit.
/// The value must be strictly positive.
/// </summary>
public sealed class CreditPayment
{
    public CreditPayment(Guid id, Money value, DateOnly date)
    {
        if (!value.IsPositive)
        {
            throw new NonPositiveAmountException("Credit payment value", value.Amount);
        }

        Id = id;
        Value = value;
        Date = date;
    }

    public Guid Id { get; }

    public Money Value { get; }

    /// <summary>The payment date. It belongs to the period that contains it (RN-M02).</summary>
    public DateOnly Date { get; }
}
