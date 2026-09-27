using FluentAssertions;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Time;

public class DateRangeTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void DaysIncludeBothEnds()
    {
        new DateRange(D(2026, 7, 1), D(2026, 9, 30)).Days.Should().Be(92);
        new DateRange(D(2026, 10, 1), D(2026, 12, 31)).Days.Should().Be(92);
        new DateRange(D(2026, 5, 5), D(2026, 5, 5)).Days.Should().Be(1);
        new DateRange(D(2024, 2, 1), D(2024, 2, 29)).Days.Should().Be(29); // leap year
    }

    [Fact]
    public void S8_DaysBetweenReadings()
    {
        // S8: 22. 5. 2023 → 1. 11. 2023 is 163 days, → 19. 1. 2025 is 608 days (end-exclusive differences).
        new DateRange(D(2023, 5, 22), D(2023, 11, 1)).Days.Should().Be(164);
        new DateRange(D(2023, 5, 22), D(2025, 1, 19)).Days.Should().Be(609);
    }

    [Fact]
    public void RejectsReversedRange()
    {
        var act = () => new DateRange(D(2026, 2, 2), D(2026, 2, 1));
        act.Should().Throw<ArgumentException>().WithMessage("*2026-02-02 > 2026-02-01*");
    }

    [Fact]
    public void ContainsBothEnds()
    {
        var r = new DateRange(D(2026, 1, 1), D(2026, 6, 30));
        r.Contains(D(2026, 1, 1)).Should().BeTrue();
        r.Contains(D(2026, 6, 30)).Should().BeTrue();
        r.Contains(D(2025, 12, 31)).Should().BeFalse();
        r.Contains(D(2026, 7, 1)).Should().BeFalse();
    }

    [Fact]
    public void OverlapOnASharedEndDay()
    {
        var a = new DateRange(D(2026, 1, 1), D(2026, 3, 31));
        a.Overlaps(new DateRange(D(2026, 3, 31), D(2026, 4, 30))).Should().BeTrue();
        a.Overlaps(new DateRange(D(2026, 4, 1), D(2026, 4, 30))).Should().BeFalse();
        a.Intersect(new DateRange(D(2026, 3, 31), D(2026, 4, 30))).Should().Be(new DateRange(D(2026, 3, 31), D(2026, 3, 31)));
        a.Intersect(new DateRange(D(2026, 4, 1), D(2026, 4, 30))).Should().BeNull();
    }

    [Fact]
    public void S3_SplitAtJoinDay()
    {
        // S3: settlement 1. 7.–31. 12., house E joins 1. 10. → 92 + 92 days.
        var parts = new DateRange(D(2026, 7, 1), D(2026, 12, 31)).SplitAt(D(2026, 10, 1));
        parts.Should().Equal(new DateRange(D(2026, 7, 1), D(2026, 9, 30)), new DateRange(D(2026, 10, 1), D(2026, 12, 31)));
        parts.Select(p => p.Days).Should().Equal(92, 92);
    }

    [Fact]
    public void SplitOnFirstDayOrOutsideKeepsRangeWhole()
    {
        var r = new DateRange(D(2026, 1, 1), D(2026, 1, 31));
        r.SplitAt(D(2026, 1, 1)).Should().Equal(r);
        r.SplitAt(D(2026, 2, 1)).Should().Equal(r);
        r.SplitAt(D(2025, 12, 1)).Should().Equal(r);
        r.SplitAt(D(2026, 1, 31)).Should().Equal(new DateRange(D(2026, 1, 1), D(2026, 1, 30)), new DateRange(D(2026, 1, 31), D(2026, 1, 31)));
    }

    [Fact]
    public void FormatsAsIsoRange()
    {
        new DateRange(D(2026, 1, 1), D(2026, 6, 30)).ToString().Should().Be("2026-01-01–2026-06-30");
    }
}
