using MiSaldo.Application.Abstractions;

namespace MiSaldo.Infrastructure.Tests;

/// <summary>A clock tests can set and advance.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset? now = null) => UtcNow = now ?? DateTimeOffset.UtcNow;

    public DateTimeOffset UtcNow { get; set; }

    public DateOnly Today(TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, timeZone).DateTime);
}
