using MiSaldo.Domain.Common;

namespace MiSaldo.Domain.Periods;

/// <summary>
/// A financial period with a fixed type and date range. Once created, its type
/// and dates never change in response to later configuration changes
/// (RF-PRD-04, RN-P06). Indicators are computed elsewhere; this entity only
/// owns the period's identity, range, status and closure.
/// </summary>
public sealed class FinancialPeriod
{
    public FinancialPeriod(
        Guid id,
        PeriodType type,
        DateOnly startDate,
        DateOnly endDate,
        bool carryOverEnabled)
    {
        if (endDate < startDate)
        {
            throw new InvalidPeriodRangeException(startDate, endDate);
        }

        Id = id;
        Type = type;
        StartDate = startDate;
        EndDate = endDate;
        CarryOverEnabled = carryOverEnabled;
    }

    public Guid Id { get; }

    public PeriodType Type { get; }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }

    /// <summary>Whether a positive final balance carries over to the next period.</summary>
    public bool CarryOverEnabled { get; }

    public bool IsClosed { get; private set; }

    /// <summary>True when the given date falls within the period, inclusive.</summary>
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>
    /// The period's status relative to <paramref name="today"/> (RF-PRD-05, D-02).
    /// A closed period is always <see cref="PeriodStatus.Closed"/>.
    /// </summary>
    public PeriodStatus StatusOn(DateOnly today)
    {
        if (IsClosed)
        {
            return PeriodStatus.Closed;
        }

        if (today < StartDate)
        {
            return PeriodStatus.Planned;
        }

        if (today > EndDate)
        {
            return PeriodStatus.Overdue;
        }

        return PeriodStatus.Active;
    }

    /// <summary>Marks the period as closed; its figures become final (RF-PRD-06, RN-P04).</summary>
    public void Close() => IsClosed = true;

    /// <summary>
    /// The amount that carries over to the next period given this period's final
    /// balance. Only a positive balance carries over, and only when carry-over
    /// is enabled; otherwise the carry-over is zero (RF-PRD-06, RN-P05, D-03).
    /// </summary>
    public Money CarryOverFor(Money finalBalance)
    {
        if (CarryOverEnabled && finalBalance.IsPositive)
        {
            return finalBalance;
        }

        return Money.Zero(finalBalance.Currency);
    }
}
