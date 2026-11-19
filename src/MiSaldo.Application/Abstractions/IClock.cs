namespace MiSaldo.Application.Abstractions;

/// <summary>
/// Source of the current time. Use cases never read the system clock directly,
/// so they stay deterministic in tests. "Today" depends on the user's time zone
/// (RNF-DAT-06), so it is computed per zone.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>The current calendar date in the given time zone.</summary>
    DateOnly Today(TimeZoneInfo timeZone);
}
