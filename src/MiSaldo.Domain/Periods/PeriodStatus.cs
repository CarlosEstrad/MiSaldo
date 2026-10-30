namespace MiSaldo.Domain.Periods;

/// <summary>
/// The state of a financial period relative to a reference date (RF-PRD-05, D-02).
/// </summary>
public enum PeriodStatus
{
    /// <summary>The period starts in the future.</summary>
    Planned,

    /// <summary>The reference date falls within the period.</summary>
    Active,

    /// <summary>The period has ended but has not been closed by the user.</summary>
    Overdue,

    /// <summary>The user has closed the period; its figures are final.</summary>
    Closed,
}
