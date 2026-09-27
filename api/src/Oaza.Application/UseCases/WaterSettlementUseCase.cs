using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Application.UseCases;

/// <summary>
/// Water PVK and water losses over a period (T05): gathers readings, participation, PVK invoices and the losses
/// rule and runs <see cref="WaterSettlementCalculator"/>. The result shows, for every interval between main meter
/// readings, the consumption, the loss, the price from the invoices, the method used for the losses and each
/// house's cost — and how it reconciles with the invoices.
/// </summary>
public class WaterSettlementUseCase
{
    /// <summary>Longest overview (5 years).</summary>
    private const int MaxRangeDays = 1830;

    private readonly ICostComponentRepository _components;
    private readonly IComponentAllocationRuleRepository _rules;
    private readonly IParticipationRepository _participations;
    private readonly ICostEntryRepository _entries;
    private readonly IWaterMeterRepository _meters;
    private readonly IMeterReadingRepository _readings;
    private readonly IHouseRepository _houses;

    public WaterSettlementUseCase(
        ICostComponentRepository components,
        IComponentAllocationRuleRepository rules,
        IParticipationRepository participations,
        ICostEntryRepository entries,
        IWaterMeterRepository meters,
        IMeterReadingRepository readings,
        IHouseRepository houses)
    {
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _meters = meters ?? throw new ArgumentNullException(nameof(meters));
        _readings = readings ?? throw new ArgumentNullException(nameof(readings));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
    }

    public async Task<WaterSettlementResponse> CalculateAsync(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new AppException("Datum od musí být nejpozději v den data do.");
        var range = new DateRange(from, to);
        if (range.Days > MaxRangeDays)
            throw new AppException("Přehled může mít nejvýš 5 let.");

        var components = await _components.GetAllComponentsAsync();
        var water = components.FirstOrDefault(c => c.WaterRole == WaterRole.Consumption)
            ?? throw new BusinessRuleException(["Chybí nákladová složka pro vodu PVK (role „Voda PVK“)."]);
        var losses = components.FirstOrDefault(c => c.WaterRole == WaterRole.Losses);

        var meters = await _meters.GetByPartitionKeyAsync(PartitionKeys.Meter);
        var main = meters.FirstOrDefault(m => m.Type == MeterType.Main)
            ?? throw new BusinessRuleException(["Chybí hlavní vodoměr."]);
        var houseNames = (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name);

        var houseMeters = new List<HouseMeter>();
        foreach (var meter in meters.Where(m => m.Type == MeterType.Individual && m.HouseId is not null))
            houseMeters.Add(new HouseMeter(meter.HouseId!, houseNames.GetValueOrDefault(meter.HouseId!, meter.HouseId!), await _readings.GetByMeterIdAsync(meter.Id)));

        var input = new WaterSettlementInput(
            MainReadings: await _readings.GetByMeterIdAsync(main.Id),
            HouseMeters: houseMeters,
            WaterParticipations: await _participations.GetByComponentAsync(water.Id),
            Invoices: await _entries.GetByComponentAsync(water.Id),
            LossRules: losses is null ? [] : await _rules.GetByComponentAsync(losses.Id),
            LossParticipations: losses is null ? [] : await _participations.GetByComponentAsync(losses.Id));

        var intervals = WaterSettlementCalculator.Calculate(input, range);
        var name = (string id) => houseNames.GetValueOrDefault(id, id);

        return new WaterSettlementResponse
        {
            From = from,
            To = to,
            ConsumptionComponentName = water.Name,
            LossComponentName = losses?.Name,
            Intervals = intervals.Select(i => new WaterIntervalResponse
            {
                From = i.Range.From,
                To = i.Range.To,
                Days = i.Range.Days,
                MainConsumptionM3 = i.MainConsumptionM3,
                HousesConsumptionM3 = i.Houses.Sum(h => h.ConsumptionM3),
                LossM3 = i.LossM3,
                PricePerM3 = i.PricePerM3 is { } p ? decimal.Round(p, 4, MidpointRounding.AwayFromZero) : null,
                InvoicedAmount = i.InvoicedAmount,
                InvoicedM3 = i.InvoicedM3,
                WaterCost = i.WaterCost,
                LossCost = i.LossCost,
                Difference = i.Allocated ? i.InvoicedAmount - i.WaterCost - i.LossCost : 0m,
                Allocated = i.Allocated,
                Warnings = i.Warnings.ToList(),
                LossSegments = i.LossShares
                    .GroupBy(s => s.Segment)
                    .OrderBy(g => g.Key.From)
                    .Select(g => new LossSegmentResponse
                    {
                        From = g.Key.From,
                        To = g.Key.To,
                        Days = g.Key.Days,
                        Method = g.First().Method,
                        Amount = g.First().SegmentAmount,
                        Participants = g.Count(),
                    })
                    .ToList(),
                Houses = i.Houses
                    .Select(h => ToResponse(h.HouseId, name(h.HouseId), h.ConsumptionM3, h.IsEstimate, h.WaterCost, h.LossCost))
                    .OrderBy(h => h.HouseName, StringComparer.CurrentCulture)
                    .ToList(),
            }).ToList(),
            Totals = intervals
                .Where(i => i.Allocated)
                .SelectMany(i => i.Houses)
                .GroupBy(h => h.HouseId)
                .Select(g => ToResponse(g.Key, name(g.Key), g.Sum(h => h.ConsumptionM3), g.Any(h => h.IsEstimate), g.Sum(h => h.WaterCost), g.Sum(h => h.LossCost)))
                .OrderBy(h => h.HouseName, StringComparer.CurrentCulture)
                .ToList(),
        };
    }

    private static WaterHouseResponse ToResponse(string houseId, string houseName, decimal consumption, bool estimate, decimal water, decimal loss) => new()
    {
        HouseId = houseId,
        HouseName = houseName,
        ConsumptionM3 = consumption,
        IsEstimate = estimate,
        WaterCost = water,
        LossCost = loss,
    };
}
