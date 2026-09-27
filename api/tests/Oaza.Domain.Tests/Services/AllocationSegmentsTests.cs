using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Services;

public class AllocationSegmentsTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static Participation P(string house, DateOnly from, DateOnly? to = null, decimal? weight = null) =>
        new() { Id = $"{house}-{from:yyyyMMdd}", ComponentId = "c", HouseId = house, ValidFrom = from, ValidTo = to, Weight = weight };

    private static ComponentAllocationRule R(AllocationMethod method, DateOnly from, DateOnly? to = null) =>
        new() { Id = $"r-{from:yyyyMMdd}", ComponentId = "c", Method = method, ValidFrom = from, ValidTo = to };

    [Fact]
    public void S3_HouseJoiningSplitsTheSettlementPeriod92To92()
    {
        var from = D(2025, 1, 1);
        var participations = new[] { P("A", from), P("B", from), P("C", from), P("D", from), P("E", D(2026, 10, 1)) };

        var segments = AllocationSegments.Build(
            new DateRange(D(2026, 7, 1), D(2026, 12, 31)), [R(AllocationMethod.Equal, from)], participations);

        segments.Should().HaveCount(2);
        segments[0].Range.Should().Be(new DateRange(D(2026, 7, 1), D(2026, 9, 30)));
        segments[0].Range.Days.Should().Be(92);
        segments[0].Participants.Select(p => p.HouseId).Should().Equal("A", "B", "C", "D");
        segments[1].Range.Should().Be(new DateRange(D(2026, 10, 1), D(2026, 12, 31)));
        segments[1].Range.Days.Should().Be(92);
        segments[1].Participants.Select(p => p.HouseId).Should().Equal("A", "B", "C", "D", "E");
        segments.Should().OnlyContain(s => s.Rule!.Method == AllocationMethod.Equal);
    }

    [Fact]
    public void WaterworksFourHousesFromStart_OneSegment()
    {
        var start = D(2023, 11, 1);
        var segments = AllocationSegments.Build(
            new DateRange(start, D(2023, 12, 31)),
            [R(AllocationMethod.Equal, start)],
            [P("A", start), P("B", start), P("C", start), P("D", start)]);

        segments.Should().ContainSingle();
        segments[0].Participants.Should().HaveCount(4);
    }

    [Fact]
    public void RuleChangeAndParticipationEndCreateBoundaries()
    {
        var segments = AllocationSegments.Build(
            new DateRange(D(2026, 1, 1), D(2026, 12, 31)),
            [R(AllocationMethod.Equal, D(2025, 1, 1), D(2026, 9, 30)), R(AllocationMethod.Ratio, D(2026, 10, 1))],
            [P("A", D(2025, 1, 1)), P("B", D(2025, 1, 1), D(2026, 3, 31))]);

        segments.Select(s => s.Range).Should().Equal(
            new DateRange(D(2026, 1, 1), D(2026, 3, 31)),
            new DateRange(D(2026, 4, 1), D(2026, 9, 30)),
            new DateRange(D(2026, 10, 1), D(2026, 12, 31)));
        segments.Select(s => s.Participants.Count).Should().Equal(2, 1, 1);
        segments.Select(s => s.Rule!.Method).Should().Equal(AllocationMethod.Equal, AllocationMethod.Equal, AllocationMethod.Ratio);
    }

    [Fact]
    public void OneDaySegment()
    {
        var segments = AllocationSegments.Build(
            new DateRange(D(2026, 5, 1), D(2026, 5, 31)),
            [R(AllocationMethod.Equal, D(2026, 1, 1))],
            [P("A", D(2026, 1, 1)), P("B", D(2026, 5, 15), D(2026, 5, 15))]);

        segments.Select(s => s.Range.Days).Should().Equal(14, 1, 16);
        segments[1].Participants.Select(p => p.HouseId).Should().Equal("A", "B");
    }

    [Fact]
    public void BoundariesOutsideTheRangeAreIgnored_AndMissingRuleIsNull()
    {
        var segments = AllocationSegments.Build(
            new DateRange(D(2026, 1, 1), D(2026, 1, 31)),
            [],
            [P("A", D(2020, 1, 1), D(2030, 1, 1)), P("B", D(2026, 2, 1))]);

        segments.Should().ContainSingle();
        segments[0].Rule.Should().BeNull();
        segments[0].Participants.Select(p => p.HouseId).Should().Equal("A");
    }

    [Theory]
    [InlineData(2026, 1, 1, true)]
    [InlineData(2026, 6, 30, true)]
    [InlineData(2025, 12, 31, false)]
    [InlineData(2026, 7, 1, false)]
    public void IsActiveIncludesBothEnds(int y, int m, int d, bool expected)
    {
        AllocationSegments.IsActive(D(2026, 1, 1), D(2026, 6, 30), D(y, m, d)).Should().Be(expected);
        AllocationSegments.IsActive(D(2026, 1, 1), null, D(2030, 1, 1)).Should().BeTrue();
    }
}
