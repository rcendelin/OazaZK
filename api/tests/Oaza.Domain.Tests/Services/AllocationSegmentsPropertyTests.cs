using FsCheck;
using FsCheck.Xunit;
using Oaza.Domain.Entities;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Services;

/// <summary>Property-based tests of <see cref="AllocationSegments"/> on random participations around 2024–2026.</summary>
public class AllocationSegmentsPropertyTests
{
    private static readonly DateOnly Base = new(2024, 1, 1);

    private static List<Participation> Participations((NonNegativeInt Start, NonNegativeInt Length, bool Open)[] raw) =>
        raw.Select((r, i) => new Participation
        {
            HouseId = $"H{i % 6}-{i}",
            ValidFrom = Base.AddDays(r.Start.Get % 900),
            ValidTo = r.Open ? null : Base.AddDays(r.Start.Get % 900 + r.Length.Get % 400),
        }).ToList();

    [Property(MaxTest = 300)]
    public bool SegmentsCoverTheRangeAndParticipationIsConstantInside(
        NonNegativeInt start, NonNegativeInt length, (NonNegativeInt, NonNegativeInt, bool)[] raw)
    {
        var range = new DateRange(Base.AddDays(start.Get % 900), Base.AddDays(start.Get % 900 + length.Get % 500));
        var participations = Participations(raw);
        var segments = AllocationSegments.Build(range, [], participations);

        var covers = segments.Sum(s => s.Range.Days) == range.Days
            && segments[0].Range.From == range.From
            && segments[^1].Range.To == range.To
            && segments.Zip(segments.Skip(1), (a, b) => a.Range.To.AddDays(1) == b.Range.From).All(x => x);

        var constant = segments.All(s => participations.All(p =>
        {
            var inside = s.Participants.Contains(p);
            return AllocationSegments.IsActive(p.ValidFrom, p.ValidTo, s.Range.From) == inside
                && AllocationSegments.IsActive(p.ValidFrom, p.ValidTo, s.Range.To) == inside;
        }));

        return covers && constant;
    }
}
