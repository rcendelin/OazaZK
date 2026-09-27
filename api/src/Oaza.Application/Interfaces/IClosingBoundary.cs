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
}
