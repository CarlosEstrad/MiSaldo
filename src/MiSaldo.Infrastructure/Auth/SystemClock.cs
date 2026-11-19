using MiSaldo.Application.Abstractions;

namespace MiSaldo.Infrastructure.Auth;

/// <summary>The real clock. Use cases get the time through <see cref="IClock"/>, never directly.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today(TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, timeZone).DateTime);
    }
}
