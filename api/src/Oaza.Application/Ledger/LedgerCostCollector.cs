using System.Globalization;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Application.Ledger;

/// <summary>Kind of a ledger line (T07).</summary>
public enum LedgerItemKind
{
    /// <summary>The house's fund share at the start (T03, R5).</summary>
    Opening,

    /// <summary>A payment of the house (advance, extra payment) — increases the saldo.</summary>
    Payment,

    /// <summary>Money paid back to the house — decreases the saldo.</summary>
    Payout,

    /// <summary>The house's share of a component cost (T06).</summary>
    Cost,

    /// <summary>The house's share of a component credit with the supplier (T03, O2) — a negative cost.</summary>
    Credit,

    /// <summary>Water PVK by metered consumption (T05).</summary>
    Water,

    /// <summary>The house's share of the water loss (T05).</summary>
    Loss,
}

/// <summary>How a cost share came about — the drill-down a member sees (T07).</summary>
/// <param name="Total">The whole amount being allocated (entry, credit, interval water or loss).</param>
/// <param name="Segment">Days of the segment the share belongs to.</param>
/// <param name="SegmentAmount">Part of the total in the segment (pro-rata by days).</param>
/// <param name="Method">Allocation method in the segment.</param>
/// <param name="Weight">The house's weight (1 for EQUAL, m³ for consumption, % for PERCENT).</param>
/// <param name="TotalWeight">Sum of the weights of all houses in the segment.</param>
/// <param name="Explanation">The calculation in words (Czech).</param>
public record LedgerCostDetail(
    decimal Total,
    DateRange Segment,
    decimal SegmentAmount,
    AllocationMethod Method,
    decimal Weight,
    decimal TotalWeight,
    string Explanation);

/// <summary>A house's cost (positive) or credit (negative) from one component on one segment.</summary>
public record LedgerCost(
    string HouseId,
    DateOnly Date,
    LedgerItemKind Kind,
    string ComponentId,
    string ComponentName,
    string Description,
    decimal Amount,
    LedgerCostDetail Detail);

/// <summary>A component's allocated total in the range vs. the sum over houses — the control row (T07).</summary>
public record ComponentControl(string ComponentId, string ComponentName, decimal AllocatedTotal, decimal HousesTotal, IReadOnlyList<string> Warnings)
{
    public bool Matches => AllocatedTotal == HousesTotal;
}

/// <summary>
/// Collects every house's costs in a range (T07): component cost entries allocated by segment and month (only the
/// segments inside the range count), component credits (T03) and water PVK and losses per interval between main meter
/// readings (T05, the interval counts in the range where it starts). One source for the house ledger, the overview and
/// its control row.
/// </summary>
public class LedgerCostCollector
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private readonly ICostComponentRepository _components;
    private readonly IComponentAllocationRuleRepository _rules;
    private readonly IParticipationRepository _participations;
    private readonly ICostEntryRepository _entries;
    private readonly IOpeningBalanceRepository _openingBalances;
    private readonly IWaterMeterRepository _meters;
    private readonly IMeterReadingRepository _readings;
    private readonly IHouseRepository _houses;

    public LedgerCostCollector(
        ICostComponentRepository components,
        IComponentAllocationRuleRepository rules,
        IParticipationRepository participations,
        ICostEntryRepository entries,
        IOpeningBalanceRepository openingBalances,
        IWaterMeterRepository meters,
        IMeterReadingRepository readings,
        IHouseRepository houses)
    {
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _openingBalances = openingBalances ?? throw new ArgumentNullException(nameof(openingBalances));
        _meters = meters ?? throw new ArgumentNullException(nameof(meters));
        _readings = readings ?? throw new ArgumentNullException(nameof(readings));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
    }

    public record Result(IReadOnlyList<LedgerCost> Costs, IReadOnlyList<ComponentControl> Controls);

    public async Task<Result> CollectAsync(DateRange range)
    {
        var components = (await _components.GetAllComponentsAsync()).OrderBy(c => c.Name, StringComparer.CurrentCulture).ToList();
        var openings = await _openingBalances.GetAllBalancesAsync();
        var costs = new List<LedgerCost>();
        var controls = new List<ComponentControl>();

        foreach (var component in components.Where(c => c.AllocationBasis == AllocationBasis.CostEntries))
        {
            var rules = await _rules.GetByComponentAsync(component.Id);
            var participations = await _participations.GetByComponentAsync(component.Id);
            var warnings = new List<string>();
            var allocated = 0m;
            var before = costs.Count;

            foreach (var entry in (await _entries.GetByComponentAsync(component.Id))
                         .Where(e => e.PostingDate is { } posted ? range.Contains(posted) : e.PeriodTo >= range.From && e.PeriodFrom <= range.To))
            {
                var period = new DateRange(entry.PeriodFrom, entry.PeriodTo);
                var cuts = CostEntryAllocation.MonthStarts(period).Append(range.From).Append(range.To.AddDays(1));
                IReadOnlyList<CostShare> shares;
                try
                {
                    shares = CostEntryAllocation.Allocate(entry.Amount, period, rules, participations, cuts);
                }
                catch (InvalidOperationException ex)
                {
                    warnings.Add($"{EntryLabel(entry)}: {ex.Message}");
                    continue;
                }

                // A correction booked after an interim closing counts whole, on its posting day (T08).
                var inRange = entry.PostingDate is null ? shares.Where(s => range.Contains(s.Segment.From)).ToList() : shares.ToList();
                var label = entry.PostingDate is null ? EntryLabel(entry) : $"{EntryLabel(entry)} — opravný záznam po mezizávěrce";
                allocated += inRange.GroupBy(s => s.Segment).Sum(g => g.First().SegmentAmount);
                costs.AddRange(inRange.Select(s => new LedgerCost(
                    s.HouseId, entry.PostingDate ?? s.Segment.From, LedgerItemKind.Cost, component.Id, component.Name, label, s.Amount,
                    Detail(entry.Amount, s, inRange.Where(x => x.Segment == s.Segment).Sum(x => x.Weight),
                        $"{EntryLabel(entry)} {Kc(entry.Amount)} za {entry.PeriodTo.DayNumber - entry.PeriodFrom.DayNumber + 1} dní"))));
            }

            foreach (var credit in openings.Where(o => o.Type == OpeningBalanceType.ComponentCredit && o.ComponentId == component.Id && range.Contains(o.Date)))
            {
                var day = new DateRange(credit.Date, credit.Date);
                var segment = AllocationSegments.Build(day, rules, participations).Single();
                IReadOnlyList<HouseShare> shares;
                try
                {
                    shares = SegmentAllocation.Split(credit.Value, segment);
                }
                catch (InvalidOperationException ex)
                {
                    warnings.Add($"Kredit u dodavatele k {Day(credit.Date)}: {ex.Message}");
                    continue;
                }
                allocated += credit.Value;
                var totalWeight = shares.Sum(s => s.Weight);
                costs.AddRange(shares.Select(s => new LedgerCost(
                    s.Participation.HouseId, credit.Date, LedgerItemKind.Credit, component.Id, component.Name,
                    "Kredit u dodavatele (počáteční stav)", s.Amount,
                    new LedgerCostDetail(credit.Value, day, credit.Value, segment.Rule!.Method, s.Weight, totalWeight,
                        $"Kredit {Kc(credit.Value)} k {Day(credit.Date)} ({credit.Source}), {Method(segment.Rule.Method)} mezi {shares.Count} domů: {Share(s.Weight, totalWeight)} = {Kc(s.Amount)}"))));
            }

            controls.Add(new ComponentControl(component.Id, component.Name, allocated, costs.Skip(before).Sum(c => c.Amount), warnings));
        }

        await CollectWaterAsync(components, range, costs, controls);
        return new Result(costs, controls);
    }

    private async Task CollectWaterAsync(List<CostComponent> components, DateRange range, List<LedgerCost> costs, List<ComponentControl> controls)
    {
        var water = components.FirstOrDefault(c => c.WaterRole == WaterRole.Consumption);
        if (water is null)
            return;
        var losses = components.FirstOrDefault(c => c.WaterRole == WaterRole.Losses);
        var meters = await _meters.GetByPartitionKeyAsync(PartitionKeys.Meter);
        var main = meters.FirstOrDefault(m => m.Type == MeterType.Main);
        if (main is null)
        {
            controls.Add(new ComponentControl(water.Id, water.Name, 0m, 0m, ["Chybí hlavní vodoměr."]));
            return;
        }

        var names = (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name);
        var houseMeters = new List<HouseMeter>();
        foreach (var meter in meters.Where(m => m.Type == MeterType.Individual && m.HouseId is not null))
            houseMeters.Add(new HouseMeter(meter.HouseId!, names.GetValueOrDefault(meter.HouseId!, meter.HouseId!), await _readings.GetByMeterIdAsync(meter.Id)));

        var input = new WaterSettlementInput(
            await _readings.GetByMeterIdAsync(main.Id),
            houseMeters,
            await _participations.GetByComponentAsync(water.Id),
            await _entries.GetByComponentAsync(water.Id),
            losses is null ? [] : await _rules.GetByComponentAsync(losses.Id),
            losses is null ? [] : await _participations.GetByComponentAsync(losses.Id));

        var waterWarnings = new List<string>();
        var lossWarnings = new List<string>();
        decimal waterTotal = 0m, lossTotal = 0m;
        var waterBefore = costs.Count;
        var lossCosts = new List<LedgerCost>();
        foreach (var interval in WaterSettlementCalculator.Calculate(input, range).Where(i => range.Contains(i.Range.From)))
        {
            var label = $"úsek {Day(interval.Range.From)} – {Day(interval.Range.To)}";
            if (!interval.Allocated)
            {
                waterWarnings.Add($"{label} není rozpočtený: {string.Join(" ", interval.Warnings)}");
                continue;
            }
            lossWarnings.AddRange(interval.Warnings.Select(w => $"{label}: {w}"));
            waterTotal += interval.WaterCost;
            lossTotal += interval.LossCost;
            var totalConsumption = interval.Houses.Sum(h => h.ConsumptionM3);
            foreach (var house in interval.Houses.Where(h => h.WaterCost != 0))
            {
                costs.Add(new LedgerCost(house.HouseId, interval.Range.From, LedgerItemKind.Water, water.Id, water.Name, $"Voda, {label}", house.WaterCost,
                    new LedgerCostDetail(interval.WaterCost, interval.Range, interval.WaterCost, AllocationMethod.Metered, house.ConsumptionM3, totalConsumption,
                        $"Spotřeba {M3(house.ConsumptionM3)}{(house.IsEstimate ? " (odhad)" : string.Empty)} × {Kc(interval.PricePerM3!.Value)}/m³ (cena z faktur PVK) = {Kc(house.WaterCost)}")));
            }
            foreach (var share in interval.LossShares)
            {
                var segmentWeight = interval.LossShares.Where(s => s.Segment == share.Segment).Sum(s => s.Weight);
                lossCosts.Add(new LedgerCost(share.HouseId, share.Segment.From, LedgerItemKind.Loss, losses!.Id, losses.Name, $"Ztráta vody, {label}", share.Amount,
                    Detail(interval.LossCost, share, segmentWeight,
                        $"Ztráta {M3(interval.LossM3)} × {Kc(interval.PricePerM3!.Value)}/m³ = {Kc(interval.LossCost)}")));
            }
        }

        controls.Add(new ComponentControl(water.Id, water.Name, waterTotal, costs.Skip(waterBefore).Sum(c => c.Amount), waterWarnings));
        if (losses is not null)
        {
            costs.AddRange(lossCosts);
            controls.Add(new ComponentControl(losses.Id, losses.Name, lossTotal, lossCosts.Sum(c => c.Amount), lossWarnings));
        }
    }

    private static LedgerCostDetail Detail(decimal total, CostShare share, decimal totalWeight, string what) =>
        new(total, share.Segment, share.SegmentAmount, share.Method, share.Weight, totalWeight,
            $"{what}; úsek {Day(share.Segment.From)} – {Day(share.Segment.To)} ({share.Segment.Days} dní) = {Kc(share.SegmentAmount)}, " +
            $"{Method(share.Method)}: {Share(share.Weight, totalWeight)} = {Kc(share.Amount)}");

    private static string EntryLabel(CostEntry entry) => entry.Type switch
    {
        CostEntryType.Advance => $"Záloha {Day(entry.PeriodFrom)} – {Day(entry.PeriodTo)}",
        CostEntryType.Settlement => $"Vyúčtování {Day(entry.PeriodFrom)} – {Day(entry.PeriodTo)}",
        _ => $"Náklad {Day(entry.PeriodFrom)} – {Day(entry.PeriodTo)}",
    } + (entry.Supplier is null ? string.Empty : $" ({entry.Supplier})");

    private static string Method(AllocationMethod method) => method switch
    {
        AllocationMethod.Equal => "rovným dílem",
        AllocationMethod.Ratio => "poměrem",
        AllocationMethod.Percent => "procenty",
        _ => "podle spotřeby",
    };

    private static string Share(decimal weight, decimal total) =>
        $"podíl {weight.ToString("0.###", Czech)} z {total.ToString("0.###", Czech)}";

    private static string Kc(decimal value) => $"{value.ToString("N2", Czech)} Kč";

    private static string M3(decimal value) => $"{value.ToString("0.###", Czech)} m³";

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);
}
