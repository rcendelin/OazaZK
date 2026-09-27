using FsCheck;
using FsCheck.Xunit;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Time;

/// <summary>Property-based tests of <see cref="DateRange"/> on random ranges around 2020–2030.</summary>
public class DateRangePropertyTests
{
    private static readonly DateOnly Base = new(2020, 1, 1);

    private static DateRange Range(NonNegativeInt start, NonNegativeInt length) =>
        new(Base.AddDays(start.Get % 3650), Base.AddDays(start.Get % 3650 + length.Get % 800));

    [Property(MaxTest = 500)]
    public bool SplitPartsCoverTheRangeWithoutGaps(NonNegativeInt start, NonNegativeInt length, int cutOffset)
    {
        var range = Range(start, length);
        var cut = range.From.AddDays(cutOffset % (range.Days + 10));
        var parts = range.SplitAt(cut);

        var contiguous = parts.Zip(parts.Skip(1), (a, b) => a.To.AddDays(1) == b.From).All(x => x);
        return parts.Sum(p => p.Days) == range.Days
            && parts[0].From == range.From
            && parts[^1].To == range.To
            && contiguous;
    }

    [Property(MaxTest = 500)]
    public bool IntersectionIsInsideBothAndMatchesOverlap(NonNegativeInt s1, NonNegativeInt l1, NonNegativeInt s2, NonNegativeInt l2)
    {
        var a = Range(s1, l1);
        var b = Range(s2, l2);
        var i = a.Intersect(b);
        if (i is null)
            return !a.Overlaps(b);
        return a.Overlaps(b)
            && a.Contains(i.Value.From) && a.Contains(i.Value.To)
            && b.Contains(i.Value.From) && b.Contains(i.Value.To);
    }
}
