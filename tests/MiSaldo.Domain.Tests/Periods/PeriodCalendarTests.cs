using MiSaldo.Domain.Periods;
using Xunit;

namespace MiSaldo.Domain.Tests.Periods;

public class PeriodCalendarTests
{
    // --- RN-P01: weekly ---

    [Fact]
    public void Weekly_starts_on_configured_week_start_monday()
    {
        var settings = new PeriodSettings(weekStart: DayOfWeek.Monday);
        // 2026-01-14 is a Wednesday; the week starting Monday is Jan 12..18.
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Weekly, new DateOnly(2026, 1, 14), settings);
        Assert.Equal(new DateOnly(2026, 1, 12), start);
        Assert.Equal(new DateOnly(2026, 1, 18), end);
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
    }

    [Fact]
    public void Weekly_respects_a_sunday_week_start()
    {
        var settings = new PeriodSettings(weekStart: DayOfWeek.Sunday);
        // 2026-01-14 Wednesday; week starting Sunday is Jan 11..17.
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Weekly, new DateOnly(2026, 1, 14), settings);
        Assert.Equal(new DateOnly(2026, 1, 11), start);
        Assert.Equal(new DateOnly(2026, 1, 17), end);
    }

    [Fact]
    public void Weekly_anchor_on_start_day_keeps_that_day_as_start()
    {
        var settings = new PeriodSettings(weekStart: DayOfWeek.Monday);
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Weekly, new DateOnly(2026, 1, 12), settings); // Monday
        Assert.Equal(new DateOnly(2026, 1, 12), start);
        Assert.Equal(new DateOnly(2026, 1, 18), end);
    }

    // --- RN-P02: biweekly, default B=16 ---

    [Fact]
    public void Biweekly_default_cycle1_is_1_to_15()
    {
        var settings = new PeriodSettings(); // B = 16
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Biweekly, new DateOnly(2026, 1, 10), settings);
        Assert.Equal(new DateOnly(2026, 1, 1), start);
        Assert.Equal(new DateOnly(2026, 1, 15), end);
    }

    [Fact]
    public void Biweekly_default_cycle2_is_16_to_end_of_month()
    {
        var settings = new PeriodSettings(); // B = 16
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Biweekly, new DateOnly(2026, 1, 20), settings);
        Assert.Equal(new DateOnly(2026, 1, 16), start);
        Assert.Equal(new DateOnly(2026, 1, 31), end);
    }

    [Fact]
    public void Biweekly_cycle2_end_follows_shorter_month()
    {
        var settings = new PeriodSettings(); // B = 16
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Biweekly, new DateOnly(2026, 2, 20), settings);
        Assert.Equal(new DateOnly(2026, 2, 16), start);
        Assert.Equal(new DateOnly(2026, 2, 28), end); // 2026 is not a leap year
    }

    [Fact]
    public void Biweekly_boundary_day_belongs_to_cycle2()
    {
        var settings = new PeriodSettings(); // B = 16
        var (start, _) = PeriodCalendar.RangeFor(
            PeriodType.Biweekly, new DateOnly(2026, 1, 16), settings);
        Assert.Equal(new DateOnly(2026, 1, 16), start);
    }

    [Fact]
    public void Biweekly_with_non_default_b_splits_accordingly()
    {
        var settings = new PeriodSettings(biweeklySecondCycleStartDay: 11); // cycle1 1..10, cycle2 11..end
        var (c1Start, c1End) = PeriodCalendar.RangeFor(
            PeriodType.Biweekly, new DateOnly(2026, 1, 5), settings);
        Assert.Equal(new DateOnly(2026, 1, 1), c1Start);
        Assert.Equal(new DateOnly(2026, 1, 10), c1End);

        var (c2Start, c2End) = PeriodCalendar.RangeFor(
            PeriodType.Biweekly, new DateOnly(2026, 1, 25), settings);
        Assert.Equal(new DateOnly(2026, 1, 11), c2Start);
        Assert.Equal(new DateOnly(2026, 1, 31), c2End);
    }

    // --- RN-P03: monthly ---

    [Fact]
    public void Monthly_default_is_first_to_last_day()
    {
        var settings = new PeriodSettings(); // monthly start day 1
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Monthly, new DateOnly(2026, 3, 10), settings);
        Assert.Equal(new DateOnly(2026, 3, 1), start);
        Assert.Equal(new DateOnly(2026, 3, 31), end);
    }

    [Fact]
    public void Monthly_with_custom_start_day_anchor_after_start()
    {
        var settings = new PeriodSettings(monthlyStartDay: 25);
        // Anchor Jan 28 -> period Jan 25 .. Feb 24.
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Monthly, new DateOnly(2026, 1, 28), settings);
        Assert.Equal(new DateOnly(2026, 1, 25), start);
        Assert.Equal(new DateOnly(2026, 2, 24), end);
    }

    [Fact]
    public void Monthly_with_custom_start_day_anchor_before_start_uses_previous_month()
    {
        var settings = new PeriodSettings(monthlyStartDay: 25);
        // Anchor Feb 10 -> period Jan 25 .. Feb 24.
        var (start, end) = PeriodCalendar.RangeFor(
            PeriodType.Monthly, new DateOnly(2026, 2, 10), settings);
        Assert.Equal(new DateOnly(2026, 1, 25), start);
        Assert.Equal(new DateOnly(2026, 2, 24), end);
    }

    // --- Settings validation ---

    [Theory]
    [InlineData(1)]
    [InlineData(29)]
    public void Invalid_biweekly_start_day_is_rejected(int b)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PeriodSettings(biweeklySecondCycleStartDay: b));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void Invalid_monthly_start_day_is_rejected(int day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PeriodSettings(monthlyStartDay: day));
    }
}
