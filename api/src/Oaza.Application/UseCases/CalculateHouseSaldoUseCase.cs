using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

/// <summary>
/// Computes each house's running saldo, split into water / electricity / common
/// base, by summing every billing period's per-component (charge − paid).
///
/// Charges are locked per period: closed periods use the stored
/// <see cref="Settlement"/> snapshot, the open period uses a live settlement
/// preview. The PAID side is always recomputed live from the payment ledger, so
/// a doplatek recorded after a period closes still moves the saldo (it never
/// mutates the stored settlement). Payments that fall outside every period are
/// reported in a separate "unassigned" bucket. Saldo = Charged − Paid
/// (positive = nedoplatek, negative = přeplatek).
///
/// This is the period-based model: the timeline is boxed by billing periods, so
/// no per-house start anchor is needed.
/// </summary>
public class CalculateHouseSaldoUseCase
{
    private const string UnassignedPeriodId = "__unassigned__";

    private readonly CalculateSettlementUseCase _calculateSettlementUseCase;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IAdvanceSettingsRepository _advanceSettingsRepository;
    private readonly ILogger<CalculateHouseSaldoUseCase> _logger;

    public CalculateHouseSaldoUseCase(
        CalculateSettlementUseCase calculateSettlementUseCase,
        IBillingPeriodRepository billingPeriodRepository,
        ISettlementRepository settlementRepository,
        IHouseRepository houseRepository,
        IAdvancePaymentRepository advanceRepository,
        IAdvanceSettingsRepository advanceSettingsRepository,
        ILogger<CalculateHouseSaldoUseCase> logger)
    {
        _calculateSettlementUseCase = calculateSettlementUseCase ?? throw new ArgumentNullException(nameof(calculateSettlementUseCase));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _settlementRepository = settlementRepository ?? throw new ArgumentNullException(nameof(settlementRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _advanceSettingsRepository = advanceSettingsRepository ?? throw new ArgumentNullException(nameof(advanceSettingsRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates saldo for a single house (when <paramref name="houseIdFilter"/>
    /// is set) or every active house (when null).
    /// </summary>
    public async Task<List<HouseSaldoResponse>> CalculateAsync(string? houseIdFilter)
    {
        var allHouses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        var targetHouses = allHouses
            .Where(h => houseIdFilter == null ? h.IsActive : h.Id == houseIdFilter)
            .ToList();

        if (targetHouses.Count == 0)
        {
            return new List<HouseSaldoResponse>();
        }

        var periods = (await _billingPeriodRepository.GetByPartitionKeyAsync(PartitionKeys.Period))
            .OrderBy(p => p.DateFrom)
            .ToList();

        var settings = await _advanceSettingsRepository.GetAsync();
        var lossMethod = Enum.TryParse<LossAllocationMethod>(settings.LossAllocationMethod, ignoreCase: true, out var m)
            ? m
            : LossAllocationMethod.ProportionalToConsumption;

        // Per period: houseId -> locked per-component CHARGES (closed = snapshot, open = preview).
        var chargesByPeriod = new Dictionary<string, Dictionary<string, ComponentTriple>>();
        foreach (var period in periods)
        {
            chargesByPeriod[period.Id] = period.Status == BillingPeriodStatus.Closed
                ? await LoadClosedChargesAsync(period)
                : await ComputeOpenChargesAsync(period, lossMethod);
        }

        var result = new List<HouseSaldoResponse>();
        foreach (var house in targetHouses.OrderBy(h => h.Name))
        {
            result.Add(await BuildHouseSaldoAsync(house, periods, chargesByPeriod, settings));
        }

        return result;
    }

    private async Task<HouseSaldoResponse> BuildHouseSaldoAsync(
        House house,
        List<BillingPeriod> periods,
        Dictionary<string, Dictionary<string, ComponentTriple>> chargesByPeriod,
        AdvanceSettings settings)
    {
        var payments = await _advanceRepository.GetByHouseIdAsync(house.Id);

        // Component-split money-in (advances + doplatky) is bucketed per period for
        // the analytical water/electricity/common breakdown.
        var paidByPeriod = new Dictionary<string, ComponentTriple>();
        // Net-level transactions (payouts, opening balance) move the total only.
        decimal netAdjustments = 0m;
        var adjustments = new List<SaldoAdjustment>();

        foreach (var p in payments)
        {
            if (p.Type is PaymentType.Advance or PaymentType.Doplatek)
            {
                var effective = p.EffectiveDate();
                var period = periods.FirstOrDefault(per => effective >= per.DateFrom && effective <= per.DateTo);
                var key = period?.Id ?? UnassignedPeriodId;
                paidByPeriod.TryGetValue(key, out var acc);
                paidByPeriod[key] = acc.Add(p.WaterAmount, p.ElectricityAmount, p.CommonAmount);
            }
            else
            {
                // Payout: Amount > 0 (refund of overpayment → increases saldo TOWARD ZERO).
                // OpeningBalance: signed Amount (positive = debt/nedoplatek, negative = credit/přeplatek).
                netAdjustments += p.Amount;
                adjustments.Add(new SaldoAdjustment(
                    Type: p.Type.ToString(),
                    Amount: p.Amount,
                    Date: p.PaymentDate,
                    Note: p.Note,
                    RowKey: p.RowKey));
            }
        }

        var totalWater = new ComponentAccumulator();
        var totalElec = new ComponentAccumulator();
        var totalCommon = new ComponentAccumulator();
        var breakdown = new List<PeriodSaldoBreakdown>();

        foreach (var period in periods)
        {
            chargesByPeriod[period.Id].TryGetValue(house.Id, out var charge);
            paidByPeriod.TryGetValue(period.Id, out var paid);

            if (charge.IsZero && paid.IsZero) continue;

            totalWater.Add(charge.Water, paid.Water);
            totalElec.Add(charge.Electricity, paid.Electricity);
            totalCommon.Add(charge.Common, paid.Common);

            breakdown.Add(new PeriodSaldoBreakdown(
                PeriodId: period.Id,
                PeriodName: period.Name,
                Closed: period.Status == BillingPeriodStatus.Closed,
                Water: Component(charge.Water, paid.Water),
                Electricity: Component(charge.Electricity, paid.Electricity),
                Common: Component(charge.Common, paid.Common)));
        }

        // Component payments that fall outside every period are pure credit (no charge).
        if (paidByPeriod.TryGetValue(UnassignedPeriodId, out var unassigned) && !unassigned.IsZero)
        {
            totalWater.Add(0m, unassigned.Water);
            totalElec.Add(0m, unassigned.Electricity);
            totalCommon.Add(0m, unassigned.Common);

            breakdown.Add(new PeriodSaldoBreakdown(
                PeriodId: UnassignedPeriodId,
                PeriodName: "Nezařazené platby",
                Closed: false,
                Water: Component(0m, unassigned.Water),
                Electricity: Component(0m, unassigned.Electricity),
                Common: Component(0m, unassigned.Common)));
        }

        var water = totalWater.ToComponent();
        var electricity = totalElec.ToComponent();
        var common = totalCommon.ToComponent();
        var componentSaldo = Math.Round(water.Saldo + electricity.Saldo + common.Saldo, 2);
        netAdjustments = Math.Round(netAdjustments, 2);
        var totalSaldo = Math.Round(componentSaldo + netAdjustments, 2);

        // Prescribed monthly = the house's actual override (water + electricity + common).
        var prescribedMonthly = settings.HouseOverrides.TryGetValue(house.Id, out var ov)
            ? Math.Round(ov.WaterAdvance + ov.ElectricityAdvance + ov.CommonAdvance, 2)
            : 0m;

        // "Overpayment lasts ~N months" — only meaningful when in credit with a prescribed amount.
        decimal? monthsCovered = totalSaldo < 0 && prescribedMonthly > 0
            ? Math.Round(-totalSaldo / prescribedMonthly, 1)
            : null;

        return new HouseSaldoResponse(
            HouseId: house.Id,
            HouseName: house.Name,
            Water: water,
            Electricity: electricity,
            Common: common,
            ComponentSaldo: componentSaldo,
            NetAdjustments: netAdjustments,
            TotalSaldo: totalSaldo,
            PrescribedMonthly: prescribedMonthly,
            MonthsCovered: monthsCovered,
            Dissolving: house.DissolveOverpayment,
            Periods: breakdown,
            Adjustments: adjustments.OrderByDescending(a => a.Date).ToList());
    }

    private static SaldoComponent Component(decimal charged, decimal paid) =>
        new(Math.Round(charged, 2), Math.Round(paid, 2), Math.Round(charged - paid, 2));

    private async Task<Dictionary<string, ComponentTriple>> LoadClosedChargesAsync(BillingPeriod period)
    {
        var settlements = await _settlementRepository.GetByPeriodIdAsync(period.Id);
        return settlements.ToDictionary(
            s => s.HouseId,
            s => new ComponentTriple(s.CalculatedAmount, s.ElectricityCharge, s.CommonCharge));
    }

    private async Task<Dictionary<string, ComponentTriple>> ComputeOpenChargesAsync(
        BillingPeriod period, LossAllocationMethod lossMethod)
    {
        try
        {
            var preview = await _calculateSettlementUseCase.CalculateAsync(period.Id, lossMethod);
            return preview.Houses.ToDictionary(
                h => h.HouseId,
                h => new ComponentTriple(h.CalculatedAmount, h.ElectricityCharge, h.CommonCharge));
        }
        catch (Exception ex)
        {
            // Open period without enough readings to settle water — its charges are
            // skipped, but payments still count (via the paid buckets / unassigned).
            _logger.LogWarning(ex,
                "Could not compute open-period charges for period {PeriodId} ({Name}).",
                period.Id, period.Name);
            return new Dictionary<string, ComponentTriple>();
        }
    }

    private readonly record struct ComponentTriple(decimal Water, decimal Electricity, decimal Common)
    {
        public bool IsZero => Water == 0m && Electricity == 0m && Common == 0m;

        public ComponentTriple Add(decimal water, decimal electricity, decimal common) =>
            new(Water + water, Electricity + electricity, Common + common);
    }

    private sealed class ComponentAccumulator
    {
        public decimal Charged { get; private set; }
        public decimal Paid { get; private set; }

        public void Add(decimal charged, decimal paid)
        {
            Charged += charged;
            Paid += paid;
        }

        public SaldoComponent ToComponent() =>
            new(Math.Round(Charged, 2), Math.Round(Paid, 2), Math.Round(Charged - Paid, 2));
    }
}
