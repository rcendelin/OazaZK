using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Services;

public class CostEntryAllocationTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static Participation P(string house, DateOnly from, DateOnly? to = null) =>
        new() { ComponentId = "c", HouseId = house, ValidFrom = from, ValidTo = to };

    private static readonly ComponentAllocationRule EqualFromStart = new() { ComponentId = "c", Method = AllocationMethod.Equal, ValidFrom = D(2023, 11, 1) };

    private static Dictionary<string, decimal> Totals(IEnumerable<CostShare> shares) =>
        shares.GroupBy(s => s.HouseId).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));

    [Fact]
    public void S3_SettlementSplitsIntoSegmentsByDays_AndTheJoiningHouseBearsOnlyTheSecond()
    {
        var start = D(2023, 11, 1);
        var participations = new[] { P("A", start), P("B", start), P("C", start), P("D", start), P("E", D(2026, 10, 1)) };

        var shares = CostEntryAllocation.Allocate(1_840m, new DateRange(D(2026, 7, 1), D(2026, 12, 31)), [EqualFromStart], participations);

        shares.Select(s => s.Segment).Distinct().Should().Equal(
            new DateRange(D(2026, 7, 1), D(2026, 9, 30)), new DateRange(D(2026, 10, 1), D(2026, 12, 31)));
        shares.Where(s => s.Segment.From == D(2026, 7, 1)).Should().OnlyContain(s => s.SegmentAmount == 920m && s.Amount == 230m);
        shares.Where(s => s.Segment.From == D(2026, 10, 1)).Should().OnlyContain(s => s.SegmentAmount == 920m && s.Amount == 184m);
        Totals(shares).Should().BeEquivalentTo(new Dictionary<string, decimal> { ["A"] = 414m, ["B"] = 414m, ["C"] = 414m, ["D"] = 414m, ["E"] = 184m });
    }

    [Fact]
    public void S3_WithMonthCutsTheTotalsStayTheSame()
    {
        var start = D(2023, 11, 1);
        var period = new DateRange(D(2026, 7, 1), D(2026, 12, 31));
        var participations = new[] { P("A", start), P("B", start), P("C", start), P("D", start), P("E", D(2026, 10, 1)) };

        var shares = CostEntryAllocation.Allocate(1_840m, period, [EqualFromStart], participations, CostEntryAllocation.MonthStarts(period));

        shares.Select(s => s.Segment).Distinct().Should().HaveCount(6);
        shares.Sum(s => s.Amount).Should().Be(1_840m);
        Totals(shares).Should().BeEquivalentTo(new Dictionary<string, decimal> { ["A"] = 414m, ["B"] = 414m, ["C"] = 414m, ["D"] = 414m, ["E"] = 184m });
    }

    [Fact]
    public void S2_MonthlyAdvanceIs125PerWaterworksHouse_WhateverPaidIt()
    {
        var start = D(2023, 11, 1);
        var shares = CostEntryAllocation.Allocate(500m, new DateRange(D(2023, 11, 1), D(2023, 11, 30)), [EqualFromStart],
            [P("A", start), P("B", start), P("C", start), P("D", start)]);

        shares.Select(s => (s.HouseId, s.Amount)).Should().Equal(("A", 125m), ("B", 125m), ("C", 125m), ("D", 125m));
    }

    [Fact]
    public void QuarterlyAdvanceSpreadsOverMonthsByDays_LeapYear()
    {
        var period = new DateRange(D(2024, 1, 1), D(2024, 3, 31)); // 31 + 29 + 31 = 91 days
        var shares = CostEntryAllocation.Allocate(910m, period, [EqualFromStart], [P("A", D(2023, 11, 1))], CostEntryAllocation.MonthStarts(period));

        shares.Select(s => (s.Segment.Days, s.Amount)).Should().Equal((31, 310m), (29, 290m), (31, 310m));
    }

    [Fact]
    public void RoundingAlwaysSumsToTheAmount()
    {
        var period = new DateRange(D(2025, 1, 15), D(2025, 4, 10));
        var shares = CostEntryAllocation.Allocate(1_000m, period, [EqualFromStart],
            [P("A", D(2023, 11, 1)), P("B", D(2023, 11, 1)), P("C", D(2025, 2, 20))], CostEntryAllocation.MonthStarts(period));

        shares.Sum(s => s.Amount).Should().Be(1_000m);
        shares.Should().OnlyContain(s => decimal.Round(s.Amount, 2) == s.Amount);
    }

    [Fact]
    public void MonthStartsInsideThePeriod()
    {
        CostEntryAllocation.MonthStarts(new DateRange(D(2026, 7, 15), D(2026, 10, 1)))
            .Should().Equal(D(2026, 8, 1), D(2026, 9, 1), D(2026, 10, 1));
        CostEntryAllocation.MonthStarts(new DateRange(D(2026, 7, 1), D(2026, 7, 31))).Should().BeEmpty();
    }

    [Fact]
    public void NobodyParticipatingIsReported()
    {
        var act = () => CostEntryAllocation.Allocate(100m, new DateRange(D(2026, 1, 1), D(2026, 1, 31)), [EqualFromStart], []);
        act.Should().Throw<InvalidOperationException>().WithMessage("*žádný dům*");
    }
}
