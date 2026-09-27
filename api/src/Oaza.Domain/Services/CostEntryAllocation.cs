using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Time;

namespace Oaza.Domain.Services;

/// <param name="HouseId">The house.</param>
/// <param name="Segment">Days of the segment the share belongs to.</param>
/// <param name="Method">Allocation method in the segment.</param>
/// <param name="SegmentAmount">Part of the whole amount falling into the segment (pro-rata by days).</param>
/// <param name="Weight">The house's weight within the segment.</param>
/// <param name="Amount">The house's share in CZK.</param>
public record CostShare(string HouseId, DateRange Segment, AllocationMethod Method, decimal SegmentAmount, decimal Weight, decimal Amount);

/// <summary>
/// Allocates a cost entry of a component to houses (T06, T08): the entry's period is cut into segments in which
/// rules and participation are constant (plus optional extra cuts, e.g. month ends or interim closings), the
/// amount is spread over the segments pro-rata by days and each segment's part is split among its participants.
/// Every step rounds by the largest remainder, so the shares always sum exactly to the entry's amount.
/// </summary>
public static class CostEntryAllocation
{
    public static IReadOnlyList<CostShare> Allocate(
        decimal amount,
        DateRange period,
        IEnumerable<ComponentAllocationRule> rules,
        IEnumerable<Participation> participations,
        IEnumerable<DateOnly>? cuts = null)
    {
        var segments = Cut(AllocationSegments.Build(period, rules, participations), cuts ?? []);
        var segmentAmounts = Allocator.Allocate(amount, segments.Select(s => (decimal)s.Range.Days).ToList());

        var shares = new List<CostShare>();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            foreach (var share in SegmentAllocation.Split(segmentAmounts[i], segment))
                shares.Add(new CostShare(share.Participation.HouseId, segment.Range, segment.Rule!.Method, segmentAmounts[i], share.Weight, share.Amount));
        }
        return shares;
    }

    /// <summary>First days of calendar months inside <paramref name="period"/> — cuts that spread a cost per month (R6).</summary>
    public static IEnumerable<DateOnly> MonthStarts(DateRange period)
    {
        var month = new DateOnly(period.From.Year, period.From.Month, 1).AddMonths(1);
        for (; month <= period.To; month = month.AddMonths(1))
            yield return month;
    }

    private static List<AllocationSegment> Cut(IReadOnlyList<AllocationSegment> segments, IEnumerable<DateOnly> cuts)
    {
        var cutList = cuts.Distinct().OrderBy(d => d).ToList();
        var result = new List<AllocationSegment>();
        foreach (var segment in segments)
        {
            var pieces = new List<DateRange> { segment.Range };
            foreach (var cut in cutList)
                pieces = pieces.SelectMany(p => p.SplitAt(cut)).ToList();
            result.AddRange(pieces.Select(p => segment with { Range = p }));
        }
        return result;
    }
}
