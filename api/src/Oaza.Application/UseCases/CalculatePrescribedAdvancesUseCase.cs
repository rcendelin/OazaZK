using Oaza.Application.Ledger;
using Oaza.Domain.Constants;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Time;

namespace Oaza.Application.UseCases;

/// <summary>
/// Computes each active house's prescribed monthly advance. The recommendation is the house's allocated costs over
/// the last 12 months (the new model's ledger, T07) divided by 12 and rounded to whole CZK; the actual advance is the
/// admin override or the recommendation. Shared by GET /advance-settings/calculate and the bank statement import,
/// which uses the actual amounts to recognise and split regular advances.
/// </summary>
public class CalculatePrescribedAdvancesUseCase
{
    /// <summary>Length of the history the recommendation is based on.</summary>
    public const int Months = 12;

    /// <summary>Components whose code starts with this prefix count as electricity (e.g. <c>ELEKTRINA_VODARNA</c>).</summary>
    public const string ElectricityCodePrefix = "ELEKTRINA";

    private readonly IAdvanceSettingsRepository _settingsRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly ICostComponentRepository _componentRepository;
    private readonly LedgerCostCollector _costs;
    private readonly IClock _clock;

    public CalculatePrescribedAdvancesUseCase(
        IAdvanceSettingsRepository settingsRepository,
        IHouseRepository houseRepository,
        ICostComponentRepository componentRepository,
        LedgerCostCollector costs,
        IClock clock)
    {
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _componentRepository = componentRepository ?? throw new ArgumentNullException(nameof(componentRepository));
        _costs = costs ?? throw new ArgumentNullException(nameof(costs));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<PrescribedAdvancesResult> CalculateAsync()
    {
        var settings = await _settingsRepository.GetAsync();
        var today = _clock.Today;
        var period = new DateRange(today.AddMonths(-Months), today.AddDays(-1));

        var houses = (await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House)).Where(h => h.IsActive).ToList();
        var electricity = (await _componentRepository.GetAllComponentsAsync())
            .Where(c => c.Code.StartsWith(ElectricityCodePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .ToHashSet();
        var collected = await _costs.CollectAsync(period);

        var result = new List<HousePrescribedAdvance>();
        foreach (var house in houses)
        {
            // Credits with the supplier are a one-off opening item (T03), not a running cost.
            var costs = collected.Costs.Where(c => c.HouseId == house.Id && c.Kind != LedgerItemKind.Credit).ToList();
            var inPeriod = new AdvanceSplit(
                costs.Where(c => c.Kind is LedgerItemKind.Water or LedgerItemKind.Loss).Sum(c => c.Amount),
                costs.Where(c => c.Kind == LedgerItemKind.Cost && electricity.Contains(c.ComponentId)).Sum(c => c.Amount),
                costs.Where(c => c.Kind == LedgerItemKind.Cost && !electricity.Contains(c.ComponentId)).Sum(c => c.Amount));
            var recommended = new AdvanceSplit(Monthly(inPeriod.Water), Monthly(inPeriod.Electricity), Monthly(inPeriod.Common));

            var over = settings.HouseOverrides.GetValueOrDefault(house.Id);
            var actual = over is null
                ? recommended
                : new AdvanceSplit(over.WaterAdvance, over.ElectricityAdvance, over.CommonAdvance);

            result.Add(new HousePrescribedAdvance(house.Id, house.Name, inPeriod, recommended, actual, over is not null));
        }

        return new PrescribedAdvancesResult(period, Months, result);
    }

    private static decimal Monthly(decimal total) =>
        Math.Max(0m, Math.Round(total / Months, 0, MidpointRounding.AwayFromZero));
}

/// <summary>A monthly advance split into the three pricing components (CZK).</summary>
public record AdvanceSplit(decimal Water, decimal Electricity, decimal Common)
{
    public decimal Total => Water + Electricity + Common;
}

/// <param name="CostsInPeriod">The house's allocated costs over the whole history (not per month).</param>
public record HousePrescribedAdvance(
    string HouseId,
    string HouseName,
    AdvanceSplit CostsInPeriod,
    AdvanceSplit Recommended,
    AdvanceSplit Actual,
    bool HasOverride);

public record PrescribedAdvancesResult(
    DateRange Period,
    int Months,
    IReadOnlyList<HousePrescribedAdvance> Houses);
