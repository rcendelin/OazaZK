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
/// Haléře are rounded once for the whole entry (#16): each house's exact share of the entry is rounded by the largest
/// remainder, then split back over its segments — so houses with the same participation pay the same, whatever the
/// number of month cuts. A segment's amount is the sum of its houses' shares, so every total still matches exactly.
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
        var totalDays = segments.Sum(s => (decimal)s.Range.Days);

        // Exact (unrounded) share of every house in every segment.
        var exact = new List<(int Segment, HouseShare Share, decimal Value)>();
        var allocatable = 0m;
        for (var i = 0; i < segments.Count; i++)
        {
            var split = SegmentAllocation.Split(segmentAmounts[i], segments[i]);
            if (split.Count == 0)
                continue; // nobody participates — the segment's part stays unallocated, as before
            allocatable += segmentAmounts[i];
            var weights = split.Sum(x => x.Weight);
            var segmentExact = totalDays == 0 ? 0m : amount * segments[i].Range.Days / totalDays;
            foreach (var share in split)
                exact.Add((i, share, weights == 0 ? 0m : segmentExact * share.Weight / weights));
        }
        if (exact.Count == 0)
            return [];

        // Round once per house for the whole entry, then spread each house's total back over its segments.
        var houses = exact.GroupBy(x => x.Share.Participation.HouseId).ToList();
        var houseTotals = SafeAllocate(allocatable, houses.Select(g => Math.Abs(g.Sum(x => x.Value))).ToList());
        var amounts = new decimal[exact.Count];
        for (var h = 0; h < houses.Count; h++)
        {
            var items = houses[h].ToList();
            var parts = SafeAllocate(houseTotals[h], items.Select(x => Math.Abs(x.Value)).ToList());
            for (var k = 0; k < items.Count; k++)
                amounts[exact.IndexOf(items[k])] = parts[k];
        }

        var segmentSums = new decimal[segments.Count];
        for (var j = 0; j < exact.Count; j++)
            segmentSums[exact[j].Segment] += amounts[j];

        return exact.Select((x, j) => new CostShare(
                x.Share.Participation.HouseId, segments[x.Segment].Range, segments[x.Segment].Rule!.Method,
                segmentSums[x.Segment], x.Share.Weight, amounts[j]))
            .ToList();
    }

    /// <summary>Largest remainder; all-zero weights (e.g. zero-weight participants) split a zero total as zeros, else equally.</summary>
    private static decimal[] SafeAllocate(decimal total, IReadOnlyList<decimal> weights) =>
        weights.Sum() > 0 ? Allocator.Allocate(total, weights)
        : total == 0 ? new decimal[weights.Count]
        : Allocator.Allocate(total, weights.Select(_ => 1m).ToList());

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
