using System.Globalization;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Time;

namespace Oaza.Domain.Services;

/// <summary>
/// Business rules of a component's configuration (T02). Each check returns readable Czech
/// messages (empty = valid) so the API can reject a change with the exact reason.
/// </summary>
public static class ComponentValidation
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    /// <summary>Participations of one house on the component must not overlap; each interval must be well-formed.</summary>
    public static IReadOnlyList<string> CheckParticipations(IEnumerable<Participation> participations)
    {
        ArgumentNullException.ThrowIfNull(participations);
        var list = participations.ToList();
        var errors = new List<string>();

        foreach (var p in list.Where(p => p.ValidTo < p.ValidFrom))
            errors.Add($"Účast domu {p.HouseId} končí ({Day(p.ValidTo!.Value)}) dřív, než začíná ({Day(p.ValidFrom)}).");
        foreach (var p in list.Where(p => p.Weight < 0))
            errors.Add($"Váha účasti domu {p.HouseId} od {Day(p.ValidFrom)} nesmí být záporná.");

        foreach (var house in list.Where(p => !(p.ValidTo < p.ValidFrom)).GroupBy(p => p.HouseId))
        {
            var ordered = house.OrderBy(p => p.ValidFrom).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var previous = ordered[i - 1];
                if (previous.ValidTo is null || previous.ValidTo >= ordered[i].ValidFrom)
                    errors.Add($"Účast domu {house.Key} se překrývá: {Interval(previous.ValidFrom, previous.ValidTo)} a {Interval(ordered[i].ValidFrom, ordered[i].ValidTo)}.");
            }
        }
        return errors;
    }

    /// <summary>Rules of the component must not overlap; each interval must be well-formed.</summary>
    public static IReadOnlyList<string> CheckRules(IEnumerable<ComponentAllocationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var list = rules.ToList();
        var errors = new List<string>();

        foreach (var r in list.Where(r => r.ValidTo < r.ValidFrom))
            errors.Add($"Pravidlo rozpočtu končí ({Day(r.ValidTo!.Value)}) dřív, než začíná ({Day(r.ValidFrom)}).");

        var ordered = list.Where(r => !(r.ValidTo < r.ValidFrom)).OrderBy(r => r.ValidFrom).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            if (previous.ValidTo is null || previous.ValidTo >= ordered[i].ValidFrom)
                errors.Add($"Pravidla rozpočtu se překrývají: {Interval(previous.ValidFrom, previous.ValidTo)} a {Interval(ordered[i].ValidFrom, ordered[i].ValidTo)}.");
        }
        return errors;
    }

    /// <summary>
    /// Under a <see cref="AllocationMethod.Percent"/> rule the weights of the active participants must sum to
    /// exactly 100 % on every day. Participants are constant within a segment, so the check runs per segment
    /// and reports the segment's first day and the actual sum.
    /// </summary>
    public static IReadOnlyList<string> CheckPercentSums(
        IEnumerable<ComponentAllocationRule> rules,
        IEnumerable<Participation> participations)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(participations);
        var ruleList = rules.ToList();
        var participationList = participations.ToList();
        var percentRules = ruleList.Where(r => r.Method == AllocationMethod.Percent && !(r.ValidTo < r.ValidFrom)).ToList();
        if (percentRules.Count == 0)
            return [];

        var errors = new List<string>();
        foreach (var rule in percentRules)
        {
            // An open-ended rule is constant after the last boundary, so checking up to that day covers it.
            var lastBoundary = participationList
                .SelectMany(p => p.ValidTo is { } to ? new[] { p.ValidFrom, to.AddDays(1) } : new[] { p.ValidFrom })
                .Append(rule.ValidFrom)
                .Max();
            var to = rule.ValidTo ?? (lastBoundary > rule.ValidFrom ? lastBoundary : rule.ValidFrom);

            foreach (var segment in AllocationSegments.Build(new DateRange(rule.ValidFrom, to), [rule], participationList))
            {
                var sum = segment.Participants.Sum(p => p.Weight ?? 0m);
                if (sum != 100m)
                    errors.Add($"K {Day(segment.Range.From)} je součet procent účastníků {sum.ToString("0.##", Czech)} %, musí být 100 %.");
            }
        }
        return errors;
    }

    /// <summary>
    /// A change effective from <paramref name="changeFrom"/> must not reach into a closed period — the days up
    /// to <paramref name="lastClosedDay"/> are fixed by an interim closing (T08). Null = nothing is closed.
    /// </summary>
    public static IReadOnlyList<string> CheckNotClosed(DateOnly changeFrom, DateOnly? lastClosedDay) =>
        lastClosedDay is { } closed && changeFrom <= closed
            ? [$"Změna od {Day(changeFrom)} zasahuje do uzavřeného období (mezizávěrka k {Day(closed)})."]
            : [];

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", Czech);

    private static string Interval(DateOnly from, DateOnly? to) =>
        to is { } end ? $"{Day(from)}–{Day(end)}" : $"od {Day(from)}";
}
