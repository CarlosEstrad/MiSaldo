namespace MiSaldo.Domain.Periods;

/// <summary>
/// Computes the date range of a period from a type, an anchor date and the
/// user's settings (RN-P01 weekly, RN-P02 biweekly, RN-P03 monthly). The anchor
/// is any date that should fall inside the resulting period.
/// </summary>
public static class PeriodCalendar
{
    public static (DateOnly Start, DateOnly End) RangeFor(
        PeriodType type, DateOnly anchor, PeriodSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return type switch
        {
            PeriodType.Weekly => Weekly(anchor, settings),
            PeriodType.Biweekly => Biweekly(anchor, settings),
            PeriodType.Monthly => Monthly(anchor, settings),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown period type."),
        };
    }

    // RN-P01: 7 days starting on the configured week-start day.
    private static (DateOnly, DateOnly) Weekly(DateOnly anchor, PeriodSettings settings)
    {
        int diff = ((int)anchor.DayOfWeek - (int)settings.WeekStart + 7) % 7;
        DateOnly start = anchor.AddDays(-diff);
        return (start, start.AddDays(6));
    }

    // RN-P02: cycle 1 runs day 1..(B-1); cycle 2 runs day B..end of month.
    private static (DateOnly, DateOnly) Biweekly(DateOnly anchor, PeriodSettings settings)
    {
        int b = settings.BiweeklySecondCycleStartDay;
        int lastDay = DateTime.DaysInMonth(anchor.Year, anchor.Month);

        if (anchor.Day < b)
        {
            // Cycle 1
            DateOnly start = new(anchor.Year, anchor.Month, 1);
            DateOnly end = new(anchor.Year, anchor.Month, b - 1);
            return (start, end);
        }

        // Cycle 2
        DateOnly c2Start = new(anchor.Year, anchor.Month, b);
        DateOnly c2End = new(anchor.Year, anchor.Month, lastDay);
        return (c2Start, c2End);
    }

    // RN-P03: starts on MonthlyStartDay (1..28); ends the day before the same
    // start day in the following month.
    private static (DateOnly, DateOnly) Monthly(DateOnly anchor, PeriodSettings settings)
    {
        int startDay = settings.MonthlyStartDay;

        // The period that contains the anchor starts on startDay of either the
        // anchor's month (if anchor.Day >= startDay) or the previous month.
        DateOnly start = anchor.Day >= startDay
            ? new DateOnly(anchor.Year, anchor.Month, startDay)
            : new DateOnly(anchor.Year, anchor.Month, startDay).AddMonths(-1);

        DateOnly end = start.AddMonths(1).AddDays(-1);
        return (start, end);
    }
}
