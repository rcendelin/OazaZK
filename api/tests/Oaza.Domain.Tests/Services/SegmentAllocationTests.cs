using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Services;

public class SegmentAllocationTests
{
    private static readonly DateRange Day = new(new DateOnly(2023, 11, 1), new DateOnly(2023, 11, 1));

    private static Participation P(string house, decimal? weight = null) => new() { HouseId = house, Weight = weight };

    private static AllocationSegment Segment(AllocationMethod? method, params Participation[] participants) =>
        new(Day, method is null ? null : new ComponentAllocationRule { Method = method.Value }, participants);

    [Fact]
    public void S2_WaterworksCreditEqualAmongFourHouses()
    {
        var shares = SegmentAllocation.Split(-20_000m, Segment(AllocationMethod.Equal, P("A"), P("B"), P("C"), P("D")));

        shares.Select(s => (s.Participation.HouseId, s.Amount)).Should().Equal(("A", -5_000m), ("B", -5_000m), ("C", -5_000m), ("D", -5_000m));
        shares.Should().OnlyContain(s => s.Weight == 1m);
    }

    [Fact]
    public void S7_EqualAmongThreeRoundsByLargestRemainder()
    {
        SegmentAllocation.Split(1_000m, Segment(AllocationMethod.Equal, P("A"), P("B"), P("C")))
            .Select(s => s.Amount).Should().Equal(333.34m, 333.33m, 333.33m);
    }

    [Fact]
    public void PercentAndStaticRatioUseWeights()
    {
        SegmentAllocation.Split(1_000m, Segment(AllocationMethod.Percent, P("A", 60), P("B", 40)))
            .Select(s => s.Amount).Should().Equal(600m, 400m);
        SegmentAllocation.Split(100m, Segment(AllocationMethod.Ratio, P("A", 1), P("B", 3)))
            .Select(s => s.Amount).Should().Equal(25m, 75m);
    }

    [Fact]
    public void ConsumptionBasedMethodsCannotSplitAFixedAmount()
    {
        var metered = () => SegmentAllocation.Split(100m, Segment(AllocationMethod.Metered, P("A")));
        metered.Should().Throw<InvalidOperationException>().WithMessage("*z odečtů*");

        var ratioFromSource = () => SegmentAllocation.Split(100m,
            new AllocationSegment(Day, new ComponentAllocationRule { Method = AllocationMethod.Ratio, RatioSource = "VODA_PVK" }, [P("A", 1)]));
        ratioFromSource.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MissingRuleParticipantsOrWeightsAreReported()
    {
        var noRule = () => SegmentAllocation.Split(100m, Segment(null, P("A")));
        noRule.Should().Throw<InvalidOperationException>().WithMessage("*1. 11. 2023*metodu*");

        var nobody = () => SegmentAllocation.Split(100m, Segment(AllocationMethod.Equal));
        nobody.Should().Throw<InvalidOperationException>().WithMessage("*žádný dům*");

        var zero = () => SegmentAllocation.Split(100m, Segment(AllocationMethod.Percent, P("A"), P("B", 0)));
        zero.Should().Throw<InvalidOperationException>().WithMessage("*kladnou váhu*");

        var nullSegment = () => SegmentAllocation.Split(100m, null!);
        nullSegment.Should().Throw<ArgumentNullException>();
    }
}
