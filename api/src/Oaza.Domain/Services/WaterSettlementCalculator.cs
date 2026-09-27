using System.Globalization;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Time;

namespace Oaza.Domain.Services;

/// <summary>A house meter taking part in the water settlement.</summary>
public record HouseMeter(string HouseId, string HouseName, IReadOnlyList<MeterReading> Readings);

/// <summary>Everything the water settlement needs (T05); gathered by the application layer.</summary>
/// <param name="MainReadings">Readings of the main meter; consecutive ones define the settlement intervals.</param>
/// <param name="HouseMeters">Individual meters of the houses.</param>
/// <param name="WaterParticipations">Participation in water PVK — which houses' meters count.</param>
/// <param name="Invoices">Water PVK invoices (cost entries with invoiced m³) — the price per m³.</param>
/// <param name="LossRules">Allocation rules of the losses component (EQUAL / RATIO by consumption, O1).</param>
/// <param name="LossParticipations">Participation in the losses.</param>
public record WaterSettlementInput(
    IReadOnlyList<MeterReading> MainReadings,
    IReadOnlyList<HouseMeter> HouseMeters,
    IReadOnlyList<Participation> WaterParticipations,
    IReadOnlyList<CostEntry> Invoices,
    IReadOnlyList<ComponentAllocationRule> LossRules,
    IReadOnlyList<Participation> LossParticipations);

/// <param name="HouseId">The house.</param>
/// <param name="ConsumptionM3">Metered consumption in the interval.</param>
/// <param name="IsEstimate">A boundary reading of the house meter was interpolated.</param>
/// <param name="WaterCost">Consumption × price, CZK.</param>
/// <param name="LossCost">The house's share of the loss, CZK.</param>
public record HouseWater(string HouseId, decimal ConsumptionM3, bool IsEstimate, decimal WaterCost, decimal LossCost);

/// <param name="Range">Days between two main meter readings (the second reading's day excluded).</param>
/// <param name="MainConsumptionM3">Main meter consumption.</param>
/// <param name="LossM3">Main − Σ houses; negative = not allocated (check readings).</param>
/// <param name="PricePerM3">Σ invoice amounts ÷ Σ invoiced m³ over the interval (pro-rata by days); null when no invoice covers it.</param>
/// <param name="LossShares">How the loss cost split among houses per segment (drill-down).</param>
/// <param name="InvoicedAmount">PVK invoices falling into the interval (pro-rata by days), CZK — for the reconciliation.</param>
/// <param name="InvoicedM3">Invoiced m³ falling into the interval (pro-rata by days).</param>
/// <param name="Allocated">False when the interval could not be settled — see <paramref name="Warnings"/>.</param>
public record WaterInterval(
    DateRange Range,
    decimal MainConsumptionM3,
    decimal LossM3,
    decimal? PricePerM3,
    decimal InvoicedAmount,
    decimal InvoicedM3,
    decimal WaterCost,
    decimal LossCost,
    IReadOnlyList<HouseWater> Houses,
    IReadOnlyList<CostShare> LossShares,
    bool Allocated,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Water PVK and water losses (T05). For every interval between two consecutive main meter readings:
/// house consumption from the house meters (interpolated when a reading is missing on the boundary day),
/// loss = main − Σ houses, price per m³ from the PVK invoices, water cost = consumption × price for each house,
/// and the loss cost (loss × price) allocated by the losses component's rule in segments pro-rata by days —
/// EQUAL among the participants, or RATIO by their consumption in the interval. A negative loss is not
/// allocated and asks for a check of the readings.
/// </summary>
public static class WaterSettlementCalculator
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    /// <summary>Intervals whose days overlap <paramref name="range"/>.</summary>
    public static IReadOnlyList<WaterInterval> Calculate(WaterSettlementInput input, DateRange range)
    {
        ArgumentNullException.ThrowIfNull(input);
        var main = input.MainReadings.OrderBy(r => r.ReadingDate).ToList();
        var intervals = new List<WaterInterval>();
        for (var k = 0; k + 1 < main.Count; k++)
        {
            var from = DateOnly.FromDateTime(main[k].ReadingDate);
            var next = DateOnly.FromDateTime(main[k + 1].ReadingDate);
            if (next <= from)
                continue;
            var interval = new DateRange(from, next.AddDays(-1));
            if (!interval.Overlaps(range))
                continue;
            intervals.Add(CalculateInterval(input, interval, main[k].Value, main[k + 1].Value, next));
        }
        return intervals;
    }

    private static WaterInterval CalculateInterval(WaterSettlementInput input, DateRange interval, decimal mainStart, decimal mainEnd, DateOnly end)
    {
        var warnings = new List<string>();
        var houseProblem = false;
        var mainConsumption = mainEnd - mainStart;
        if (mainConsumption < 0)
            warnings.Add($"Hlavní vodoměr má na konci úseku nižší stav než na začátku ({Number(mainStart)} → {Number(mainEnd)} m³).");

        // Houses whose meter counts: participating in water PVK on any day of the interval.
        var houses = new List<(string HouseId, decimal Consumption, bool Estimate)>();
        foreach (var meter in input.HouseMeters)
        {
            if (!input.WaterParticipations.Any(p => p.HouseId == meter.HouseId && p.ValidFrom <= interval.To && (p.ValidTo is null || p.ValidTo >= interval.From)))
                continue;
            var start = ReadingEstimator.Estimate(meter.Readings, interval.From.ToDateTime(TimeOnly.MinValue));
            var stop = ReadingEstimator.Estimate(meter.Readings, end.ToDateTime(TimeOnly.MinValue));
            if (!Usable(start) || !Usable(stop))
            {
                warnings.Add($"{meter.HouseName}: chybí odečet vodoměru k {Day(Usable(start) ? end : interval.From)} a nejde ho dopočítat.");
                houseProblem = true;
                continue;
            }
            var consumption = stop.Value!.Value - start.Value!.Value;
            if (consumption < 0)
            {
                warnings.Add($"{meter.HouseName}: stav vodoměru klesl ({Number(start.Value.Value)} → {Number(stop.Value.Value)} m³).");
                houseProblem = true;
                continue;
            }
            houses.Add((meter.HouseId, consumption, start.IsEstimate || stop.IsEstimate));
        }

        var loss = mainConsumption - houses.Sum(h => h.Consumption);
        if (loss < 0)
            warnings.Add($"Záporná ztráta {Number(loss)} m³ (domy naměřily víc než hlavní vodoměr) — ztráta se nerozpočítá, zkontrolujte odečty.");

        var (invoicedAmount, invoicedM3) = Invoiced(input.Invoices, interval);
        var price = invoicedM3 > 0 ? invoicedAmount / invoicedM3 : (decimal?)null;
        if (price is null)
            warnings.Add("Úsek nepokrývá žádná faktura PVK s množstvím, cenu za m³ nelze určit.");

        var blocking = mainConsumption < 0 || price is null || houseProblem;
        if (blocking)
        {
            return new WaterInterval(interval, mainConsumption, loss, price, Round(invoicedAmount), invoicedM3, 0m, 0m,
                houses.Select(h => new HouseWater(h.HouseId, h.Consumption, h.Estimate, 0m, 0m)).ToList(), [], false, warnings);
        }

        // Water: Σ consumption × price, split by consumption (largest remainder keeps the total).
        var totalConsumption = houses.Sum(h => h.Consumption);
        var waterCost = decimal.Round(totalConsumption * price!.Value, 2, MidpointRounding.AwayFromZero);
        var waterParts = totalConsumption > 0
            ? Allocator.Allocate(waterCost, houses.Select(h => h.Consumption).ToList())
            : houses.Select(_ => 0m).ToArray();

        // Loss: loss × price by the losses rule, segment by segment.
        var lossCost = loss > 0 ? decimal.Round(loss * price.Value, 2, MidpointRounding.AwayFromZero) : 0m;
        IReadOnlyList<CostShare> lossShares = [];
        if (lossCost > 0)
        {
            try
            {
                lossShares = AllocateLoss(lossCost, interval, input, houses.ToDictionary(h => h.HouseId, h => h.Consumption));
            }
            catch (InvalidOperationException ex)
            {
                warnings.Add($"Ztrátu nelze rozpočítat: {ex.Message}");
                lossCost = 0m;
            }
        }

        var houseResults = houses.Select((h, i) => new HouseWater(
                h.HouseId, h.Consumption, h.Estimate, waterParts[i], lossShares.Where(s => s.HouseId == h.HouseId).Sum(s => s.Amount)))
            .ToList();
        // A loss participant without a counted meter still bears its loss share.
        foreach (var houseId in lossShares.Select(s => s.HouseId).Distinct().Where(id => houseResults.All(h => h.HouseId != id)))
            houseResults.Add(new HouseWater(houseId, 0m, false, 0m, lossShares.Where(s => s.HouseId == houseId).Sum(s => s.Amount)));

        return new WaterInterval(interval, mainConsumption, loss, price, Round(invoicedAmount), invoicedM3, waterCost, lossCost, houseResults, lossShares, true, warnings);
    }

    /// <summary>Loss cost pro-rata by days into segments of the losses component; RATIO with a source weighs by consumption.</summary>
    private static IReadOnlyList<CostShare> AllocateLoss(
        decimal lossCost, DateRange interval, WaterSettlementInput input, IReadOnlyDictionary<string, decimal> consumption)
    {
        var segments = AllocationSegments.Build(interval, input.LossRules, input.LossParticipations);
        var amounts = Allocator.Allocate(lossCost, segments.Select(s => (decimal)s.Range.Days).ToList());
        var shares = new List<CostShare>();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Rule is { Method: AllocationMethod.Ratio, RatioSource: not null } or { Method: AllocationMethod.Metered })
            {
                if (segment.Participants.Count == 0)
                    throw new InvalidOperationException($"ke dni {Day(segment.Range.From)} se ztrát neúčastní žádný dům.");
                var weights = segment.Participants.Select(p => consumption.GetValueOrDefault(p.HouseId)).ToList();
                if (weights.Sum() <= 0)
                    throw new InvalidOperationException("účastníci nemají v úseku žádnou spotřebu.");
                var parts = Allocator.Allocate(amounts[i], weights);
                shares.AddRange(segment.Participants.Select((p, j) =>
                    new CostShare(p.HouseId, segment.Range, AllocationMethod.Ratio, amounts[i], weights[j], parts[j])));
            }
            else
            {
                shares.AddRange(SegmentAllocation.Split(amounts[i], segment).Select(s =>
                    new CostShare(s.Participation.HouseId, segment.Range, segment.Rule!.Method, amounts[i], s.Weight, s.Amount)));
            }
        }
        return shares;
    }

    /// <summary>Price per m³ from invoices overlapping the interval, each weighted by its overlapping days.</summary>
    public static decimal? Price(IEnumerable<CostEntry> invoices, DateRange interval)
    {
        var (amount, quantity) = Invoiced(invoices, interval);
        return quantity > 0 ? amount / quantity : null;
    }

    /// <summary>Invoice amount and m³ falling into the interval, each invoice pro-rata by its overlapping days.</summary>
    public static (decimal Amount, decimal QuantityM3) Invoiced(IEnumerable<CostEntry> invoices, DateRange interval)
    {
        decimal amount = 0m, quantity = 0m;
        foreach (var invoice in invoices.Where(i => i.QuantityM3 is > 0))
        {
            var period = new DateRange(invoice.PeriodFrom, invoice.PeriodTo);
            if (period.Intersect(interval) is not { } overlap)
                continue;
            var share = (decimal)overlap.Days / period.Days;
            amount += invoice.Amount * share;
            quantity += invoice.QuantityM3!.Value * share;
        }
        return (amount, decimal.Round(quantity, 3, MidpointRounding.AwayFromZero));
    }

    private static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static bool Usable(ReadingEstimate e) =>
        e.Value is not null && e.Method is ReadingEstimateMethod.Exact or ReadingEstimateMethod.Interpolated;

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", Czech);

    private static string Number(decimal value) => value.ToString("0.###", Czech);
}
