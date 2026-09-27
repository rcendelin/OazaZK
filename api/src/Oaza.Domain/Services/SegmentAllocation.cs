using System.Globalization;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Domain.Services;

/// <param name="Participation">The participating house.</param>
/// <param name="Weight">Weight the house got (1 for EQUAL, the percentage or static weight otherwise).</param>
/// <param name="Amount">The house's part in CZK; parts sum exactly to the allocated amount.</param>
public record HouseShare(Participation Participation, decimal Weight, decimal Amount);

/// <summary>
/// Splits an amount among the participants of one <see cref="AllocationSegment"/> by the segment's rule
/// (EQUAL, PERCENT, static RATIO), rounded by <see cref="Allocator"/>. Consumption-based splits
/// (METERED, RATIO with a source) need readings and are done by the metered allocation (T05/T06).
/// </summary>
public static class SegmentAllocation
{
    public static IReadOnlyList<HouseShare> Split(decimal amount, AllocationSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var rule = segment.Rule
            ?? throw new InvalidOperationException($"Složka nemá k {Day(segment.Range.From)} nastavenou metodu rozpočtu.");
        if (segment.Participants.Count == 0)
            throw new InvalidOperationException($"Ke dni {Day(segment.Range.From)} se složky neúčastní žádný dům.");

        var weights = rule.Method switch
        {
            AllocationMethod.Equal => segment.Participants.Select(_ => 1m).ToList(),
            AllocationMethod.Percent => segment.Participants.Select(p => p.Weight ?? 0m).ToList(),
            AllocationMethod.Ratio when rule.RatioSource is null => segment.Participants.Select(p => p.Weight ?? 0m).ToList(),
            _ => throw new InvalidOperationException(
                "Metoda podle spotřeby se rozpočítává z odečtů; pevnou částku takto rozdělit nelze."),
        };
        if (weights.Sum() <= 0)
            throw new InvalidOperationException("Účastníci nemají žádnou kladnou váhu.");

        var parts = Allocator.Allocate(amount, weights);
        return segment.Participants.Select((p, i) => new HouseShare(p, weights[i], parts[i])).ToList();
    }

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);
}
