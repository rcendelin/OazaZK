using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

/// <summary>
/// Computes each active house's prescribed monthly advance — the recommended
/// split (from average consumption, loss share, electricity coefficient and the
/// common base fee) and the actual one (admin override or recommended).
/// Shared by GET /advance-settings/calculate and the bank statement import,
/// which uses the actual amounts to recognise and split regular advances.
/// </summary>
public class CalculatePrescribedAdvancesUseCase
{
    private readonly IAdvanceSettingsRepository _settingsRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IWaterMeterRepository _meterRepository;
    private readonly IMeterReadingRepository _readingRepository;

    public CalculatePrescribedAdvancesUseCase(
        IAdvanceSettingsRepository settingsRepository,
        IHouseRepository houseRepository,
        IWaterMeterRepository meterRepository,
        IMeterReadingRepository readingRepository)
    {
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _meterRepository = meterRepository ?? throw new ArgumentNullException(nameof(meterRepository));
        _readingRepository = readingRepository ?? throw new ArgumentNullException(nameof(readingRepository));
    }

    public async Task<PrescribedAdvancesResult> CalculateAsync()
    {
        var settings = await _settingsRepository.GetAsync();

        var allMeters = await _meterRepository.GetByPartitionKeyAsync(PartitionKeys.Meter);
        var allHouses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        var activeHouses = allHouses.Where(h => h.IsActive).ToList();
        var mainMeter = allMeters.FirstOrDefault(m => m.Type == MeterType.Main);

        // Compute average monthly consumption per house (last 3 reading intervals)
        var houseConsumptions = new Dictionary<string, decimal>();
        decimal totalConsumption = 0;

        foreach (var house in activeHouses)
        {
            var meter = allMeters.FirstOrDefault(m => m.HouseId == house.Id);
            if (meter == null) { houseConsumptions[house.Id] = 0; continue; }

            var readings = await _readingRepository.GetByMeterIdAsync(meter.Id);
            var avgMonthly = AverageMonthly(readings);

            houseConsumptions[house.Id] = Math.Max(0, avgMonthly);
            totalConsumption += Math.Max(0, avgMonthly);
        }

        // Main meter average for loss calculation
        decimal mainMonthly = 0;
        if (mainMeter != null)
        {
            mainMonthly = AverageMonthly(await _readingRepository.GetByMeterIdAsync(mainMeter.Id));
        }

        var monthlyLoss = Math.Max(0, mainMonthly - totalConsumption);

        var houses = new List<HousePrescribedAdvance>();
        foreach (var house in activeHouses)
        {
            var consumption = houseConsumptions.GetValueOrDefault(house.Id, 0);
            var share = totalConsumption > 0 ? consumption / totalConsumption : 1m / activeHouses.Count;

            // Loss allocation honors the configured method (default: proportional
            // to consumption), mirroring CalculateSettlementUseCase.AllocateLoss.
            decimal lossShare;
            if (monthlyLoss <= 0 || activeHouses.Count == 0)
            {
                lossShare = 0m;
            }
            else if (string.Equals(settings.LossAllocationMethod, "Equal", StringComparison.OrdinalIgnoreCase))
            {
                lossShare = monthlyLoss / activeHouses.Count;
            }
            else
            {
                lossShare = totalConsumption > 0
                    ? monthlyLoss * (consumption / totalConsumption)
                    : monthlyLoss / activeHouses.Count;
            }

            var totalWaterM3 = consumption + lossShare;

            // Recommended amounts
            var recWater = Math.Round(totalWaterM3 * settings.WaterPricePerM3, 0);
            var elecCoeff = settings.ElectricityCoefficients.GetValueOrDefault(house.Id, 0);
            var recElectricity = Math.Round(settings.MonthlyElectricityCost * elecCoeff / 100m, 0);
            var recCommon = settings.MonthlyCommonBaseFee;

            // Actual (admin override or recommended)
            var over = settings.HouseOverrides.GetValueOrDefault(house.Id);

            houses.Add(new HousePrescribedAdvance(
                house.Id,
                house.Name,
                AvgMonthlyM3: consumption,
                LossShareM3: lossShare,
                TotalWaterM3: totalWaterM3,
                Share: share,
                ElectricityCoefficient: elecCoeff,
                Recommended: new AdvanceSplit(recWater, recElectricity, recCommon),
                Actual: new AdvanceSplit(
                    over?.WaterAdvance ?? recWater,
                    over?.ElectricityAdvance ?? recElectricity,
                    over?.CommonAdvance ?? recCommon),
                HasOverride: over != null));
        }

        return new PrescribedAdvancesResult(settings, mainMonthly, totalConsumption, monthlyLoss, houses);
    }

    private static decimal AverageMonthly(IReadOnlyList<MeterReading> readings)
    {
        var sorted = readings.OrderByDescending(r => r.ReadingDate).Take(4).OrderBy(r => r.ReadingDate).ToList();
        if (sorted.Count < 2) return 0;

        var totalDelta = sorted.Last().Value - sorted.First().Value;
        var months = Math.Max(1, (sorted.Last().ReadingDate - sorted.First().ReadingDate).TotalDays / 30.0);
        return totalDelta / (decimal)months;
    }
}

/// <summary>A monthly advance split into the three pricing components (CZK).</summary>
public record AdvanceSplit(decimal Water, decimal Electricity, decimal Common)
{
    public decimal Total => Water + Electricity + Common;
}

public record HousePrescribedAdvance(
    string HouseId,
    string HouseName,
    decimal AvgMonthlyM3,
    decimal LossShareM3,
    decimal TotalWaterM3,
    decimal Share,
    decimal ElectricityCoefficient,
    AdvanceSplit Recommended,
    AdvanceSplit Actual,
    bool HasOverride);

public record PrescribedAdvancesResult(
    AdvanceSettings Settings,
    decimal MainMeterMonthlyM3,
    decimal TotalIndividualMonthlyM3,
    decimal MonthlyLossM3,
    IReadOnlyList<HousePrescribedAdvance> Houses);
