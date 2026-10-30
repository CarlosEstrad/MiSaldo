namespace MiSaldo.Domain.Periods;

/// <summary>
/// The user's period configuration used to compute period date ranges
/// (RF-CFG-04, RF-CFG-05). These settings only influence periods the user
/// creates from now on; existing periods keep their stored dates (RF-PRD-04).
/// </summary>
public sealed record PeriodSettings
{
    public PeriodSettings(
        DayOfWeek weekStart = DayOfWeek.Monday,
        int biweeklySecondCycleStartDay = 16,
        int monthlyStartDay = 1)
    {
        if (biweeklySecondCycleStartDay is < 2 or > 28)
        {
            throw new ArgumentOutOfRangeException(
                nameof(biweeklySecondCycleStartDay),
                biweeklySecondCycleStartDay,
                "The second biweekly cycle must start between day 2 and day 28.");
        }

        if (monthlyStartDay is < 1 or > 28)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monthlyStartDay),
                monthlyStartDay,
                "The monthly start day must be between 1 and 28.");
        }

        WeekStart = weekStart;
        BiweeklySecondCycleStartDay = biweeklySecondCycleStartDay;
        MonthlyStartDay = monthlyStartDay;
    }

    /// <summary>First day of the week for weekly periods (RN-P01).</summary>
    public DayOfWeek WeekStart { get; }

    /// <summary>
    /// Start day of the second biweekly cycle (RN-P02). Cycle 1 runs from day 1
    /// to this day minus 1; cycle 2 runs from this day to the end of the month.
    /// </summary>
    public int BiweeklySecondCycleStartDay { get; }

    /// <summary>Start day of a monthly period, 1 to 28 (RN-P03).</summary>
    public int MonthlyStartDay { get; }
}
