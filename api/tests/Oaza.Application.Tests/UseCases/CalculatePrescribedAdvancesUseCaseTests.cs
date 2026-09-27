using FluentAssertions;
using Moq;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class CalculatePrescribedAdvancesUseCaseTests
{
    private readonly Mock<IAdvanceSettingsRepository> _settingsRepo = new();
    private readonly Mock<IHouseRepository> _houseRepo = new();
    private readonly Mock<IWaterMeterRepository> _meterRepo = new();
    private readonly Mock<IMeterReadingRepository> _readingRepo = new();
    private readonly CalculatePrescribedAdvancesUseCase _sut;

    public CalculatePrescribedAdvancesUseCaseTests()
    {
        _houseRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.House)).ReturnsAsync(new List<House>
        {
            new() { Id = "h1", Name = "A", IsActive = true },
            new() { Id = "h2", Name = "B", IsActive = true },
            new() { Id = "h3", Name = "Inactive", IsActive = false },
        });
        _meterRepo.Setup(r => r.GetByPartitionKeyAsync(PartitionKeys.Meter)).ReturnsAsync(new List<WaterMeter>
        {
            new() { Id = "main", Type = MeterType.Main },
            new() { Id = "m1", Type = MeterType.Individual, HouseId = "h1" },
            new() { Id = "m2", Type = MeterType.Individual, HouseId = "h2" },
        });
        // 30-day intervals: h1 uses 10 m³/month, h2 5 m³/month, main 20 m³/month → loss 5 m³/month.
        _readingRepo.Setup(r => r.GetByMeterIdAsync("m1")).ReturnsAsync(Readings(0, 10));
        _readingRepo.Setup(r => r.GetByMeterIdAsync("m2")).ReturnsAsync(Readings(0, 5));
        _readingRepo.Setup(r => r.GetByMeterIdAsync("main")).ReturnsAsync(Readings(0, 20));
        _settingsRepo.Setup(r => r.GetAsync()).ReturnsAsync(new AdvanceSettings
        {
            WaterPricePerM3 = 100m,
            MonthlyElectricityCost = 1000m,
            ElectricityCoefficients = new() { ["h1"] = 60m, ["h2"] = 40m },
            MonthlyCommonBaseFee = 250m,
            HouseOverrides = new() { ["h2"] = new HouseAdvanceOverride { WaterAdvance = 900m, ElectricityAdvance = 400m, CommonAdvance = 200m } },
        });

        _sut = new CalculatePrescribedAdvancesUseCase(_settingsRepo.Object, _houseRepo.Object, _meterRepo.Object, _readingRepo.Object);
    }

    private static List<MeterReading> Readings(decimal from, decimal monthly) => new()
    {
        new() { ReadingDate = new DateTime(2026, 1, 1), Value = from },
        new() { ReadingDate = new DateTime(2026, 1, 31), Value = from + monthly },
    };

    [Fact]
    public async Task CalculateAsync_RecommendsFromConsumptionAndUsesOverrides()
    {
        var result = await _sut.CalculateAsync();

        result.MonthlyLossM3.Should().Be(5m);
        result.Houses.Select(h => h.HouseId).Should().Equal("h1", "h2");

        var h1 = result.Houses.Single(h => h.HouseId == "h1");
        // 10 m³ + 2/3 of the 5 m³ loss (proportional default) at 100 Kč/m³ → 1333 Kč.
        h1.Recommended.Should().Be(new AdvanceSplit(1333m, 600m, 250m));
        h1.Actual.Should().Be(h1.Recommended);
        h1.HasOverride.Should().BeFalse();

        var h2 = result.Houses.Single(h => h.HouseId == "h2");
        h2.Actual.Should().Be(new AdvanceSplit(900m, 400m, 200m));
        h2.Actual.Total.Should().Be(1500m);
        h2.HasOverride.Should().BeTrue();
    }
}
