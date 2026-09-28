using FluentAssertions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Domain.Tests.Services;

public class WaterSettlementCalculatorTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly DateOnly Start = D(2023, 11, 1);

    private static MeterReading R(string meter, DateOnly day, decimal value) =>
        new() { MeterId = meter, ReadingDate = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Value = value };

    private static Participation P(string house, DateOnly? from = null, DateOnly? to = null) =>
        new() { HouseId = house, ValidFrom = from ?? Start, ValidTo = to };

    private static CostEntry Invoice(DateOnly from, DateOnly to, decimal amount, decimal m3) =>
        new() { Type = CostEntryType.OneOff, PeriodFrom = from, PeriodTo = to, Amount = amount, QuantityM3 = m3 };

    /// <summary>S1: 1. 1.–30. 6., main 100 m³, houses 40/30/15/5, loss 10 m³ at 100 Kč/m³ = 1 000 Kč.</summary>
    private static WaterSettlementInput S1(ComponentAllocationRule lossRule, params (string House, decimal M3)[] extra)
    {
        var houses = new[] { ("A", 40m), ("B", 30m), ("C", 15m), ("D", 5m) }.Concat(extra).ToList();
        return new WaterSettlementInput(
            MainReadings: [R("main", D(2026, 1, 1), 1_000m), R("main", D(2026, 7, 1), 1_100m)],
            HouseMeters: houses.Select(h => new HouseMeter(h.Item1, $"RD {h.Item1}", [R(h.Item1, D(2026, 1, 1), 0m), R(h.Item1, D(2026, 7, 1), h.Item2)])).ToList(),
            WaterParticipations: houses.Select(h => P(h.Item1)).ToList(),
            Invoices: [Invoice(D(2026, 1, 1), D(2026, 6, 30), 10_000m, 100m)],
            LossRules: [lossRule],
            LossParticipations: houses.Select(h => P(h.Item1)).ToList());
    }

    private static readonly DateRange FirstHalf = new(D(2026, 1, 1), D(2026, 6, 30));

    [Fact]
    public void S1_Equal()
    {
        var result = WaterSettlementCalculator.Calculate(S1(new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Equal }), FirstHalf);

        var interval = result.Should().ContainSingle().Subject;
        interval.Range.Should().Be(FirstHalf);
        interval.MainConsumptionM3.Should().Be(100m);
        interval.LossM3.Should().Be(10m);
        interval.PricePerM3.Should().Be(100m);
        interval.LossCost.Should().Be(1_000m);
        interval.Allocated.Should().BeTrue();
        interval.Houses.Select(h => (h.HouseId, h.WaterCost, h.LossCost)).Should().Equal(
            ("A", 4_000m, 250m), ("B", 3_000m, 250m), ("C", 1_500m, 250m), ("D", 500m, 250m));
        interval.WaterCost.Should().Be(9_000m);
        interval.InvoicedAmount.Should().Be(10_000m);
        interval.InvoicedM3.Should().Be(100m);
        (interval.InvoicedAmount - interval.WaterCost - interval.LossCost).Should().Be(0m);
        interval.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void S1_RatioByConsumption_LargestRemainder()
    {
        var rule = new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Ratio, RatioSource = "VODA_PVK" };

        var interval = WaterSettlementCalculator.Calculate(S1(rule), FirstHalf).Single();

        interval.Houses.Select(h => h.LossCost).Should().Equal(444.44m, 333.33m, 166.67m, 55.56m);
        interval.Houses.Sum(h => h.LossCost).Should().Be(1_000m);
        interval.LossShares.Should().OnlyContain(s => s.Method == AllocationMethod.Ratio);
    }

    [Fact]
    public void MethodSwitchAppliesOnlyFromItsDate()
    {
        var input = S1(new ComponentAllocationRule { ValidFrom = Start, ValidTo = D(2026, 3, 31), Method = AllocationMethod.Equal }) with
        {
            LossRules =
            [
                new ComponentAllocationRule { ValidFrom = Start, ValidTo = D(2026, 3, 31), Method = AllocationMethod.Equal },
                new ComponentAllocationRule { ValidFrom = D(2026, 4, 1), Method = AllocationMethod.Ratio, RatioSource = "VODA_PVK" },
            ],
        };

        var interval = WaterSettlementCalculator.Calculate(input, FirstHalf).Single();

        // 181 days: 90 (Jan–Mar) EQUAL + 91 (Apr–Jun) RATIO.
        interval.LossShares.Select(s => s.Segment).Distinct().Should().Equal(
            new DateRange(D(2026, 1, 1), D(2026, 3, 31)), new DateRange(D(2026, 4, 1), D(2026, 6, 30)));
        interval.LossShares.Where(s => s.Method == AllocationMethod.Equal).Sum(s => s.Amount).Should().Be(497.24m);
        interval.LossShares.Where(s => s.Method == AllocationMethod.Ratio).Sum(s => s.Amount).Should().Be(502.76m);
        interval.Houses.Sum(h => h.LossCost).Should().Be(1_000m);
    }

    [Fact]
    public void ParticipantJoiningMidIntervalBearsLossOnlyProRata()
    {
        var input = S1(new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Equal }) with
        {
            LossParticipations = [P("A"), P("B"), P("C"), P("D"), P("E", D(2026, 4, 1))],
        };

        var interval = WaterSettlementCalculator.Calculate(input, FirstHalf).Single();

        var e = interval.Houses.Single(h => h.HouseId == "E");
        e.ConsumptionM3.Should().Be(0m);
        e.LossCost.Should().Be(100.55m); // 502.76 Kč of Apr–Jun / 5 = 100.552; the extra haléř goes to A (equal remainders → first)
        interval.Houses.Sum(h => h.LossCost).Should().Be(1_000m);
    }

    [Fact]
    public void NegativeLossIsNotAllocatedButWaterIs()
    {
        var input = S1(new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Equal }, ("E", 20m));

        var interval = WaterSettlementCalculator.Calculate(input, FirstHalf).Single();

        interval.LossM3.Should().Be(-10m);
        interval.LossCost.Should().Be(0m);
        interval.Houses.Should().OnlyContain(h => h.LossCost == 0m);
        interval.WaterCost.Should().Be(11_000m);
        interval.Allocated.Should().BeTrue();
        interval.Warnings.Should().ContainSingle().Which.Should().Contain("Záporná ztráta");
    }

    [Fact]
    public void MissingInvoiceOrReadingBlocksTheInterval()
    {
        var noInvoice = S1(new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Equal }) with { Invoices = [] };
        var a = WaterSettlementCalculator.Calculate(noInvoice, FirstHalf).Single();
        a.Allocated.Should().BeFalse();
        a.PricePerM3.Should().BeNull();
        a.Warnings.Should().Contain(w => w.Contains("faktura PVK"));

        var input = S1(new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Equal });
        var noEndReading = input with
        {
            HouseMeters = [.. input.HouseMeters.Take(3), new HouseMeter("D", "RD D", [R("D", D(2026, 1, 1), 0m)])],
        };
        var b = WaterSettlementCalculator.Calculate(noEndReading, FirstHalf).Single();
        b.Allocated.Should().BeFalse();
        b.Warnings.Should().Contain(w => w.StartsWith("RD D: chybí odečet vodoměru k 1. 7. 2026", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingBoundaryReadingIsInterpolatedAndMarkedAsEstimate()
    {
        var input = S1(new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Equal });
        // D read on 1. 1. and 31. 7. only (0 → 6 m³ over 211 days): its 1. 7. state is interpolated.
        input = input with
        {
            HouseMeters = [.. input.HouseMeters.Take(3), new HouseMeter("D", "RD D", [R("D", D(2026, 1, 1), 0m), R("D", D(2026, 7, 31), 6m)])],
        };

        var interval = WaterSettlementCalculator.Calculate(input, FirstHalf).Single();

        var d = interval.Houses.Single(h => h.HouseId == "D");
        d.IsEstimate.Should().BeTrue();
        d.ConsumptionM3.Should().Be(5.147m); // 6 × 181 / 211
        interval.Allocated.Should().BeTrue();
    }

    [Fact]
    public void IntervalsFollowMainReadings_AndPriceIsProRataOverInvoices()
    {
        var input = S1(new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Equal }) with
        {
            MainReadings = [R("main", D(2026, 1, 1), 0m), R("main", D(2026, 2, 1), 10m), R("main", D(2026, 3, 1), 20m)],
            Invoices = [Invoice(D(2026, 1, 1), D(2026, 1, 31), 1_000m, 10m), Invoice(D(2026, 2, 1), D(2026, 2, 28), 1_200m, 10m)],
        };

        var intervals = WaterSettlementCalculator.Calculate(input, new DateRange(D(2026, 1, 1), D(2026, 2, 28)));

        intervals.Select(i => i.Range).Should().Equal(
            new DateRange(D(2026, 1, 1), D(2026, 1, 31)), new DateRange(D(2026, 2, 1), D(2026, 2, 28)));
        intervals.Select(i => i.PricePerM3).Should().Equal(100m, 120m);
        WaterSettlementCalculator.Price([Invoice(D(2026, 1, 1), D(2026, 2, 28), 5_900m, 59m)], new DateRange(D(2026, 2, 1), D(2026, 2, 28)))
            .Should().Be(100m);
        WaterSettlementCalculator.Calculate(input, new DateRange(D(2026, 5, 1), D(2026, 5, 31))).Should().BeEmpty();
    }

    [Fact]
    public void MainMeterGoingBackwardsBlocks()
    {
        var input = S1(new ComponentAllocationRule { ValidFrom = Start, Method = AllocationMethod.Equal }) with
        {
            MainReadings = [R("main", D(2026, 1, 1), 1_000m), R("main", D(2026, 7, 1), 900m)],
        };

        var interval = WaterSettlementCalculator.Calculate(input, FirstHalf).Single();

        interval.Allocated.Should().BeFalse();
        interval.Warnings.Should().Contain(w => w.Contains("Hlavní vodoměr"));
    }
}
