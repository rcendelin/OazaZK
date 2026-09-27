using FluentAssertions;
using Oaza.Application.Ledger;
using Oaza.Application.Tests.TestSupport;
using Oaza.Application.UseCases;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Time;

namespace Oaza.Application.Tests.UseCases;

public class CalculatePrescribedAdvancesUseCaseTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly DateOnly Start = D(2023, 11, 1);

    private readonly MemoryComponents _components = new();
    private readonly MemoryRules _rules = new();
    private readonly MemoryParticipations _participations = new();
    private readonly MemoryCostEntries _entries = new();
    private readonly MemoryOpeningBalances _openings = new();
    private readonly MemoryMeters _meters = new();
    private readonly MemoryReadings _readings = new();
    private readonly MemoryHouses _houses = new();
    private readonly MemorySettings _settings = new();

    public CalculatePrescribedAdvancesUseCaseTests()
    {
        foreach (var name in new[] { "A", "B", "C", "D", "E", "F" })
            _houses.UpsertAsync(new House { Id = name, Name = $"RD {name}", IsActive = true });
        _houses.UpsertAsync(new House { Id = "G", Name = "RD G", IsActive = false });
    }

    /// <summary>Today is 1. 1. 2027 in Prague → the history is the whole year 2026.</summary>
    private CalculatePrescribedAdvancesUseCase Sut() => new(
        _settings, _houses, _components,
        new LedgerCostCollector(_components, _rules, _participations, _entries, _openings, _meters, _readings, _houses),
        new PragueClock(new FixedTimeProvider(new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.Zero))));

    private async Task Component(string id, string code, IEnumerable<string> houses, AllocationBasis basis = AllocationBasis.CostEntries, WaterRole role = WaterRole.None)
    {
        await _components.UpsertAsync(new CostComponent { Id = id, Name = id, Code = code, StartDate = Start, AllocationBasis = basis, WaterRole = role });
        foreach (var house in houses)
            await _participations.UpsertAsync(new Participation { Id = $"{id}-{house}", ComponentId = id, HouseId = house, ValidFrom = Start });
    }

    [Fact]
    public async Task RecommendsAllocatedCostsOfTheLast12MonthsDividedBy12_SplitIntoWaterElectricityAndCommon()
    {
        // Electricity of the waterworks: A–D, 1 200 Kč a month → 300 Kč a house a month.
        await Component("vodarna", "ELEKTRINA_VODARNA", new[] { "A", "B", "C", "D" });
        await _rules.UpsertAsync(new ComponentAllocationRule { Id = "rv", ComponentId = "vodarna", ValidFrom = Start, Method = AllocationMethod.Equal });
        for (var month = D(2026, 1, 1); month.Year == 2026; month = month.AddMonths(1))
            await _entries.UpsertAsync(new CostEntry { Id = $"v{month:MM}", ComponentId = "vodarna", Type = CostEntryType.Advance, PeriodFrom = month, PeriodTo = month.AddMonths(1).AddDays(-1), Amount = 1_200m });
        // A credit with the supplier is a one-off opening item — not part of the recommendation.
        await _openings.UpsertAsync(new OpeningBalance { Key = "ComponentCredit|vodarna|-", Type = OpeningBalanceType.ComponentCredit, ComponentId = "vodarna", Date = D(2026, 1, 1), Value = -20_000m, Source = "PRE" });

        // Common costs: all six houses, 21 900 Kč for the year (60 Kč a day) → 3 650 Kč a house → 304 Kč a month.
        await Component("spolecne", "SPOLECNE", new[] { "A", "B", "C", "D", "E", "F" });
        await _rules.UpsertAsync(new ComponentAllocationRule { Id = "rs", ComponentId = "spolecne", ValidFrom = Start, Method = AllocationMethod.Equal });
        await _entries.UpsertAsync(new CostEntry { Id = "s", ComponentId = "spolecne", Type = CostEntryType.OneOff, PeriodFrom = D(2026, 1, 1), PeriodTo = D(2026, 12, 31), Amount = 21_900m });
        // Costs before the history do not count.
        await _entries.UpsertAsync(new CostEntry { Id = "old", ComponentId = "spolecne", Type = CostEntryType.OneOff, PeriodFrom = D(2025, 1, 1), PeriodTo = D(2025, 12, 31), Amount = 60_000m });

        // Water: C used 15 of 100 m³ at 100 Kč/m³ (1 500 Kč) + its ratio share of the 10 m³ loss (166,67 Kč).
        await Component("pvk", "VODA_PVK", new[] { "A", "B", "C", "D" }, AllocationBasis.Metered, WaterRole.Consumption);
        await Component("ztraty", "ZTRATY_VODY", new[] { "A", "B", "C", "D" }, AllocationBasis.Metered, WaterRole.Losses);
        await _rules.UpsertAsync(new ComponentAllocationRule { Id = "rz", ComponentId = "ztraty", ValidFrom = Start, Method = AllocationMethod.Ratio, RatioSource = "VODA_PVK" });
        await _meters.UpsertAsync(new WaterMeter { Id = "main", MeterNumber = "H", Type = MeterType.Main });
        await Read("main", D(2026, 1, 1), 0m);
        await Read("main", D(2026, 7, 1), 100m);
        foreach (var (house, m3) in new[] { ("A", 40m), ("B", 30m), ("C", 15m), ("D", 5m) })
        {
            await _meters.UpsertAsync(new WaterMeter { Id = $"m{house}", MeterNumber = house, Type = MeterType.Individual, HouseId = house });
            await Read($"m{house}", D(2026, 1, 1), 0m);
            await Read($"m{house}", D(2026, 7, 1), m3);
        }
        await _entries.UpsertAsync(new CostEntry { Id = "f", ComponentId = "pvk", Type = CostEntryType.OneOff, PeriodFrom = D(2026, 1, 1), PeriodTo = D(2026, 6, 30), Amount = 10_000m, QuantityM3 = 100m });

        _settings.Value.HouseOverrides["F"] = new HouseAdvanceOverride { WaterAdvance = 50m, ElectricityAdvance = 0m, CommonAdvance = 150m };

        var result = await Sut().CalculateAsync();

        result.Period.Should().Be(new DateRange(D(2026, 1, 1), D(2026, 12, 31)));
        result.Months.Should().Be(12);
        result.Houses.Select(h => h.HouseId).Should().Equal("A", "B", "C", "D", "E", "F");

        var c = result.Houses.Single(h => h.HouseId == "C");
        c.CostsInPeriod.Should().Be(new AdvanceSplit(1_666.67m, 3_600m, 3_650m));
        c.Recommended.Should().Be(new AdvanceSplit(139m, 300m, 304m));
        c.Actual.Should().Be(c.Recommended);
        c.HasOverride.Should().BeFalse();

        var e = result.Houses.Single(h => h.HouseId == "E");
        e.Recommended.Should().Be(new AdvanceSplit(0m, 0m, 304m));

        var f = result.Houses.Single(h => h.HouseId == "F");
        f.Recommended.Should().Be(new AdvanceSplit(0m, 0m, 304m));
        f.Actual.Should().Be(new AdvanceSplit(50m, 0m, 150m));
        f.Actual.Total.Should().Be(200m);
        f.HasOverride.Should().BeTrue();
    }

    [Fact]
    public async Task NoHistory_RecommendsZero()
    {
        var result = await Sut().CalculateAsync();

        result.Houses.Should().HaveCount(6).And.OnlyContain(h => h.Recommended.Total == 0m && !h.HasOverride);
    }

    private Task Read(string meter, DateOnly day, decimal value) =>
        _readings.UpsertAsync(new MeterReading { MeterId = meter, ReadingDate = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Value = value });

    private sealed class MemorySettings : IAdvanceSettingsRepository
    {
        public AdvanceSettings Value { get; } = new();
        public Task<AdvanceSettings> GetAsync() => Task.FromResult(Value);
        public Task UpsertAsync(AdvanceSettings settings) => Task.CompletedTask;
    }
}
