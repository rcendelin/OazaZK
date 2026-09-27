using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Interfaces;

/// <summary>
/// The last day fixed by an interim closing (T08). Changes must not reach into it; corrections are booked
/// into the open period.
/// </summary>
public interface IClosingBoundary
{
    /// <param name="houseId">
    /// Null for changes that affect every house (component rules, participation, cost entries, readings): any closing
    /// counts. A house id for changes of that house only (its payments, its opening values): closings of all houses and
    /// of that house count.
    /// </param>
    /// <returns>The last closed day, or null when nothing is closed.</returns>
    Task<DateOnly?> GetLastClosedDayAsync(string? houseId = null);

    /// <summary>
    /// The last day closed for the whole association (closings of all houses only) — for changes that belong to no
    /// house, like the cash book. A single house's closing (a sale) must not lock them.
    /// </summary>
    Task<DateOnly?> GetLastAllHousesClosedDayAsync() => GetLastClosedDayAsync();
}

/// <summary>
/// Closed day per water meter (cached for one operation): a house meter is locked by closings of all houses and of its
/// house; the main meter by any closing (its readings feed every house's water and losses).
/// </summary>
public sealed class MeterClosingDays(IClosingBoundary boundary)
{
    private readonly Dictionary<string, DateOnly?> _byHouse = [];

    public async Task<DateOnly?> ForAsync(Oaza.Domain.Entities.WaterMeter meter)
    {
        var key = meter.Type == MeterType.Main || meter.HouseId is null ? string.Empty : meter.HouseId;
        if (!_byHouse.TryGetValue(key, out var day))
        {
            day = await boundary.GetLastClosedDayAsync(key.Length == 0 ? null : key);
            _byHouse[key] = day;
        }
        return day;
    }
}

/// <summary>Nothing is closed (tests, and before any interim closing exists).</summary>
public sealed class NoClosingBoundary : IClosingBoundary
{
    public Task<DateOnly?> GetLastClosedDayAsync(string? houseId = null) => Task.FromResult<DateOnly?>(null);
}

/// <summary>The closing boundary from the stored interim closings.</summary>
public sealed class InterimClosingBoundary : IClosingBoundary
{
    private readonly IInterimClosingRepository _closings;

    public InterimClosingBoundary(IInterimClosingRepository closings)
    {
        _closings = closings ?? throw new ArgumentNullException(nameof(closings));
    }

    public async Task<DateOnly?> GetLastClosedDayAsync(string? houseId = null)
    {
        var closings = await _closings.GetAllClosingsAsync();
        var relevant = houseId is null
            ? closings
            : closings.Where(c => c.Scope == ClosingScope.All || c.HouseId == houseId).ToList();
        return relevant.Count == 0 ? null : relevant.Max(c => c.Date);
    }

    public async Task<DateOnly?> GetLastAllHousesClosedDayAsync()
    {
        var all = (await _closings.GetAllClosingsAsync()).Where(c => c.Scope == ClosingScope.All).ToList();
        return all.Count == 0 ? null : all.Max(c => c.Date);
    }
}
