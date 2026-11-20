using MiSaldo.Application.Abstractions;

namespace MiSaldo.Api.Tests;

/// <summary>
/// The real time plus an offset tests can move forward, to cross waits such as
/// the resend cooldown without sleeping.
/// </summary>
public sealed class ShiftableClock : IClock
{
    private long _offsetTicks;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref _offsetTicks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    public DateOnly Today(TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, timeZone).DateTime);
}
