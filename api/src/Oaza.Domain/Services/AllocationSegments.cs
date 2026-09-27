using Oaza.Domain.Entities;
using Oaza.Domain.Time;

namespace Oaza.Domain.Services;

/// <param name="Range">Days of the segment, both ends included.</param>
/// <param name="Rule">The component's allocation rule valid in the whole segment; null when none is set.</param>
/// <param name="Participants">Participations active in the whole segment, ordered by house id.</param>
public record AllocationSegment(DateRange Range, ComponentAllocationRule? Rule, IReadOnlyList<Participation> Participants);

/// <summary>
/// Splits a period into segments in which a component's allocation is constant (T02):
/// the union of all boundaries of its rules and participations. Every later allocation
/// (losses T05, cost entries T06, settlements across closings T08) works per segment.
/// </summary>
public static class AllocationSegments
{
    /// <summary>
    /// Segments of <paramref name="range"/>, in order, covering it without gaps. A boundary is
    /// a <c>ValidFrom</c> or a day after a <c>ValidTo</c> of any rule or participation.
    /// </summary>
    public static IReadOnlyList<AllocationSegment> Build(
        DateRange range,
        IEnumerable<ComponentAllocationRule> rules,
        IEnumerable<Participation> participations)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(participations);
        var ruleList = rules.ToList();
        var participationList = participations.ToList();

        var end = range.To.AddDays(1);
        var boundaries = new SortedSet<DateOnly> { range.From, end };
        foreach (var (from, to) in ruleList.Select(r => (r.ValidFrom, r.ValidTo))
                     .Concat(participationList.Select(p => (p.ValidFrom, p.ValidTo))))
        {
            AddInside(boundaries, from, range.From, end);
            if (to is { } last)
                AddInside(boundaries, last.AddDays(1), range.From, end);
        }

        var starts = boundaries.ToList();
        var segments = new List<AllocationSegment>(starts.Count - 1);
        for (var i = 0; i < starts.Count - 1; i++)
        {
            var day = starts[i];
            var rule = ruleList.FirstOrDefault(r => IsActive(r.ValidFrom, r.ValidTo, day));
            var participants = participationList
                .Where(p => IsActive(p.ValidFrom, p.ValidTo, day))
                .OrderBy(p => p.HouseId, StringComparer.Ordinal)
                .ToList();
            segments.Add(new AllocationSegment(new DateRange(day, starts[i + 1].AddDays(-1)), rule, participants));
        }
        return segments;
    }

    /// <summary>True when an interval <c>[from, to]</c> (null <paramref name="to"/> = open-ended) contains <paramref name="day"/>.</summary>
    public static bool IsActive(DateOnly from, DateOnly? to, DateOnly day) => from <= day && (to is null || day <= to);

    private static void AddInside(SortedSet<DateOnly> boundaries, DateOnly day, DateOnly from, DateOnly end)
    {
        if (day > from && day < end)
            boundaries.Add(day);
    }
}
