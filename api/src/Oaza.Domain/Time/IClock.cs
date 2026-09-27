namespace Oaza.Domain.Time;

/// <summary>
/// The association's clock. <see cref="Today"/> is the calendar day in
/// <c>Europe/Prague</c> — the only valid source of "today" for accounting dates
/// (defaults, validation, document dates), whatever the server or browser zone.
/// </summary>
public interface IClock
{
    /// <summary>Current instant (UTC).</summary>
    DateTimeOffset Now { get; }

    /// <summary>Current calendar day in <c>Europe/Prague</c>.</summary>
    DateOnly Today { get; }
}
