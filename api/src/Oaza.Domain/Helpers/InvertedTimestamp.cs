namespace Oaza.Domain.Helpers;

/// <summary>
/// Provides inverted timestamp generation for Azure Table Storage RowKeys.
/// Inverted timestamps sort newest entries first (ascending RowKey order).
/// </summary>
public static class InvertedTimestamp
{
    /// <summary>
    /// Converts a DateTime to an inverted tick string for use as a RowKey.
    /// </summary>
    public static string FromDateTime(DateTime dateTime)
    {
        var invertedTicks = DateTime.MaxValue.Ticks - dateTime.Ticks;
        return invertedTicks.ToString("D19");
    }

    /// <summary>
    /// Converts an inverted tick string back to the original DateTime.
    /// Returns DateTime.MinValue if the string cannot be parsed or produces invalid ticks.
    /// </summary>
    public static DateTime ToDateTime(string invertedTimestamp)
    {
        if (!long.TryParse(invertedTimestamp, out var invertedTicks))
            return DateTime.MinValue;

        var ticks = DateTime.MaxValue.Ticks - invertedTicks;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            return DateTime.MinValue;

        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
