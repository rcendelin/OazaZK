namespace Oaza.Domain.Time;

/// <summary>
/// <see cref="IClock"/> over a <see cref="TimeProvider"/>, with "today" taken in
/// <c>Europe/Prague</c> (CET/CEST, daylight saving handled by the time zone).
/// </summary>
public sealed class PragueClock : IClock
{
    /// <summary>The association's time zone.</summary>
    public static readonly TimeZoneInfo Zone = FindPragueZone();

    /// <summary>Clock over the system time.</summary>
    public static readonly PragueClock System = new(TimeProvider.System);

    private readonly TimeProvider _timeProvider;

    public PragueClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public DateTimeOffset Now => _timeProvider.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, Zone).DateTime);

    /// <summary>
    /// <see cref="Today"/> as the midnight-UTC <see cref="DateTime"/> the old model
    /// uses for calendar days.
    /// </summary>
    public static DateTime AsUtcMidnight(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static TimeZoneInfo FindPragueZone()
    {
        // IANA id on Linux (Azure Functions), Windows id as a fallback for local dev on Windows.
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");
        }
    }
}
