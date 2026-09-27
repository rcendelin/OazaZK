using FluentAssertions;
using Oaza.Application.Exceptions;
using Oaza.Application.Tests.TestSupport;
using Oaza.Application.UseCases;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class WaterSettlementUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly DateOnly Start = D(2023, 11, 1);

    private sealed class MemoryEntries() : MemoryRepo<CostEntry>(e => e.ComponentId, e => e.Id), ICostEntryRepository
    {
        public Task<IReadOnlyList<CostEntry>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
    }

    private readonly MemoryComponents _components = new();
    private readonly MemoryRules _rules = new();
    private readonly MemoryParticipations _participations = new();
    private readonly MemoryEntries _entries = new();
    private readonly MemoryMeters _meters = new();
    private readonly MemoryReadings _readings = new();
    private readonly MemoryHouses _houses = new();

    private WaterSettlementUseCase Sut() => new(_components, _rules, _participations, _entries, _meters, _readings, _houses);

    private void Reading(string meter, DateOnly day, decimal value) =>
        _readings.UpsertAsync(new MeterReading { MeterId = meter, ReadingDate = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Value = value });

    /// <summary>S1 set-up: main 100 m³ in 1. 1.–30. 6., houses 40/30/15/5, PVK invoice 10 000 Kč for 100 m³.</summary>
    private void SeedS1(AllocationMethod lossMethod)
    {
        _components.UpsertAsync(new CostComponent { Id = "pvk", Name = "Voda PVK", Code = "VODA_PVK", StartDate = Start, AllocationBasis = AllocationBasis.Metered, WaterRole = WaterRole.Consumption });
        _components.UpsertAsync(new CostComponent { Id = "ztraty", Name = "Ztráty vody", Code = "ZTRATY_VODY", StartDate = Start, AllocationBasis = AllocationBasis.Metered, WaterRole = WaterRole.Losses });
        _rules.UpsertAsync(new ComponentAllocationRule { Id = "r", ComponentId = "ztraty", ValidFrom = Start, Method = lossMethod, RatioSource = lossMethod == AllocationMethod.Ratio ? "VODA_PVK" : null });
        _meters.UpsertAsync(new WaterMeter { Id = "main", MeterNumber = "H", Type = MeterType.Main });
        Reading("main", D(2026, 1, 1), 500m);
        Reading("main", D(2026, 7, 1), 600m);
        foreach (var (house, m3) in new[] { ("A", 40m), ("B", 30m), ("C", 15m), ("D", 5m) })
        {
            _houses.UpsertAsync(new House { Id = house, Name = $"RD {house}" });
            _meters.UpsertAsync(new WaterMeter { Id = $"m{house}", MeterNumber = house, Type = MeterType.Individual, HouseId = house });
            Reading($"m{house}", D(2026, 1, 1), 10m);
            Reading($"m{house}", D(2026, 7, 1), 10m + m3);
            _participations.UpsertAsync(new Participation { Id = $"w{house}", ComponentId = "pvk", HouseId = house, ValidFrom = Start });
            _participations.UpsertAsync(new Participation { Id = $"l{house}", ComponentId = "ztraty", HouseId = house, ValidFrom = Start });
        }
        _entries.UpsertAsync(new CostEntry { Id = "f1", ComponentId = "pvk", Type = CostEntryType.OneOff, PeriodFrom = D(2026, 1, 1), PeriodTo = D(2026, 6, 30), Amount = 10_000m, QuantityM3 = 100m });
    }

    [Theory]
    [InlineData(AllocationMethod.Equal, new[] { 250.00, 250.00, 250.00, 250.00 })]
    [InlineData(AllocationMethod.Ratio, new[] { 444.44, 333.33, 166.67, 55.56 })]
    public async Task S1_BothVariants(AllocationMethod method, double[] expectedLoss)
    {
        SeedS1(method);

        var result = await Sut().CalculateAsync(D(2026, 1, 1), D(2026, 6, 30));

        var interval = result.Intervals.Should().ContainSingle().Subject;
        interval.MainConsumptionM3.Should().Be(100m);
        interval.HousesConsumptionM3.Should().Be(90m);
        interval.LossM3.Should().Be(10m);
        interval.PricePerM3.Should().Be(100m);
        interval.LossCost.Should().Be(1_000m);
        interval.Difference.Should().Be(0m);
        interval.LossSegments.Should().ContainSingle().Which.Method.Should().Be(method);
        interval.Houses.Select(h => h.LossCost).Should().Equal(expectedLoss.Select(x => (decimal)x));
        result.Totals.Select(t => (t.HouseName, t.WaterCost)).Should().Equal(("RD A", 4_000m), ("RD B", 3_000m), ("RD C", 1_500m), ("RD D", 500m));
        result.ConsumptionComponentName.Should().Be("Voda PVK");
        result.LossComponentName.Should().Be("Ztráty vody");
    }

    [Fact]
    public async Task MissingSetupIsReported()
    {
        var noComponent = () => Sut().CalculateAsync(D(2026, 1, 1), D(2026, 6, 30));
        (await noComponent.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("vodu PVK");

        await _components.UpsertAsync(new CostComponent { Id = "pvk", Name = "Voda PVK", Code = "VODA_PVK", StartDate = Start, AllocationBasis = AllocationBasis.Metered, WaterRole = WaterRole.Consumption });
        var noMainMeter = () => Sut().CalculateAsync(D(2026, 1, 1), D(2026, 6, 30));
        (await noMainMeter.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("hlavní vodoměr");

        var reversed = () => Sut().CalculateAsync(D(2026, 2, 1), D(2026, 1, 1));
        await reversed.Should().ThrowAsync<AppException>();
        var tooLong = () => Sut().CalculateAsync(D(2000, 1, 1), D(2026, 1, 1));
        await tooLong.Should().ThrowAsync<AppException>();
    }

    [Fact]
    public async Task WithoutALossesComponentTheLossIsReportedNotAllocated()
    {
        SeedS1(AllocationMethod.Equal);
        await _components.DeleteAsync("COMPONENT", "ztraty");

        var interval = (await Sut().CalculateAsync(D(2026, 1, 1), D(2026, 6, 30))).Intervals.Single();

        interval.LossCost.Should().Be(0m);
        interval.Warnings.Should().Contain(w => w.StartsWith("Ztrátu nelze rozpočítat", StringComparison.Ordinal));
        interval.Difference.Should().Be(1_000m);
    }
}
