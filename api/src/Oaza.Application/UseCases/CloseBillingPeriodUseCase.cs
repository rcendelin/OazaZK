using Microsoft.Extensions.Logging;
using Oaza.Application.Exceptions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

/// <summary>
/// Closes a billing period: recomputes the final settlement, persists one
/// Settlement entity per house, and flips the period to Closed. This is the
/// irreversible money-committing operation (business rule #5), so it re-verifies
/// the period is still Open after calculation before writing anything.
///
/// Optionally, before the final calculation: draws an admin-chosen amount from
/// the shared common fund, split evenly across active houses as a doplatek
/// per house (see docs/superpowers/specs/2026-07-18-vodni-fond-a-cena-design.md),
/// and/or carries the period's realized invoice price/m³ forward as the new
/// recommended water-advance price.
/// </summary>
public class CloseBillingPeriodUseCase
{
    private readonly CalculateSettlementUseCase _calculateSettlementUseCase;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IFinancialRecordRepository _financialRecordRepository;
    private readonly IAdvanceSettingsRepository _advanceSettingsRepository;
    private readonly GetFundBalanceUseCase _getFundBalanceUseCase;
    private readonly ILogger<CloseBillingPeriodUseCase> _logger;

    public CloseBillingPeriodUseCase(
        CalculateSettlementUseCase calculateSettlementUseCase,
        IBillingPeriodRepository billingPeriodRepository,
        ISettlementRepository settlementRepository,
        IHouseRepository houseRepository,
        IAdvancePaymentRepository advanceRepository,
        IFinancialRecordRepository financialRecordRepository,
        IAdvanceSettingsRepository advanceSettingsRepository,
        GetFundBalanceUseCase getFundBalanceUseCase,
        ILogger<CloseBillingPeriodUseCase> logger)
    {
        _calculateSettlementUseCase = calculateSettlementUseCase ?? throw new ArgumentNullException(nameof(calculateSettlementUseCase));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _settlementRepository = settlementRepository ?? throw new ArgumentNullException(nameof(settlementRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _financialRecordRepository = financialRecordRepository ?? throw new ArgumentNullException(nameof(financialRecordRepository));
        _advanceSettingsRepository = advanceSettingsRepository ?? throw new ArgumentNullException(nameof(advanceSettingsRepository));
        _getFundBalanceUseCase = getFundBalanceUseCase ?? throw new ArgumentNullException(nameof(getFundBalanceUseCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates and persists settlements for the period, then locks it.
    /// Returns the persisted Settlement entities.
    /// </summary>
    public async Task<IReadOnlyList<Settlement>> CloseAsync(
        string periodId,
        LossAllocationMethod lossAllocationMethod,
        decimal fundDrawAmount = 0m,
        bool applyNewWaterPrice = false,
        DateTime? newWaterPriceValidFrom = null)
    {
        // Calculate final settlement numbers (also validates the period is Open).
        var preview = await _calculateSettlementUseCase.CalculateAsync(periodId, lossAllocationMethod);

        // Re-verify the period is still Open before committing the irreversible close.
        var period = await _billingPeriodRepository.GetAsync(PartitionKeys.Period, periodId)
            ?? throw new NotFoundException("BillingPeriod", periodId);

        if (period.Status != BillingPeriodStatus.Open)
        {
            throw new AppException("Zúčtovací období je již uzavřeno. Nelze jej uzavřít znovu.");
        }

        if (fundDrawAmount < 0)
        {
            throw new AppException("Čerpaná částka z fondu nesmí být záporná.");
        }

        if (fundDrawAmount > 0)
        {
            await ApplyFundDrawAsync(period, fundDrawAmount);

            // The fund doplatky now exist in the ledger — recompute so Balance
            // reflects them (CalculateSettlementUseCase itself is unchanged).
            preview = await _calculateSettlementUseCase.CalculateAsync(periodId, lossAllocationMethod);
        }

        if (applyNewWaterPrice)
        {
            await ApplyNewWaterPriceAsync(preview, newWaterPriceValidFrom);
        }

        // Save one settlement per house.
        var settlements = new List<Settlement>();
        foreach (var houseDetail in preview.Houses)
        {
            var settlement = new Settlement
            {
                PeriodId = periodId,
                HouseId = houseDetail.HouseId,
                ConsumptionM3 = houseDetail.ConsumptionM3,
                SharePercent = houseDetail.SharePercent,
                CalculatedAmount = houseDetail.CalculatedAmount,
                TotalAdvances = houseDetail.TotalAdvances,
                Balance = houseDetail.Balance,
                LossAllocatedM3 = houseDetail.LossAllocatedM3,
                ElectricityCharge = houseDetail.ElectricityCharge,
                ElectricityAdvances = houseDetail.ElectricityAdvances,
                CommonCharge = houseDetail.CommonCharge,
                CommonAdvances = houseDetail.CommonAdvances,
            };

            await _settlementRepository.UpsertAsync(settlement);
            settlements.Add(settlement);
        }

        // Lock the period (irreversible).
        period.Status = BillingPeriodStatus.Closed;
        await _billingPeriodRepository.UpsertAsync(period);

        _logger.LogInformation(
            "Billing period {PeriodId} ({Name}) closed with {Count} settlements.",
            periodId, period.Name, settlements.Count);

        return settlements;
    }

    /// <summary>
    /// Draws <paramref name="fundDrawAmount"/> from the shared common fund and
    /// splits it evenly across active houses as one doplatek each, plus one
    /// FinancialRecord expense that reduces the fund's future balance.
    /// </summary>
    private async Task ApplyFundDrawAsync(BillingPeriod period, decimal fundDrawAmount)
    {
        var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        var activeHouses = houses.Where(h => h.IsActive).ToList();

        if (activeHouses.Count == 0)
        {
            throw new AppException("Z fondu nelze čerpat: nejsou žádné aktivní domácnosti.");
        }

        var fundBalance = await _getFundBalanceUseCase.CalculateAsync();
        if (fundDrawAmount > fundBalance.FundBalance)
        {
            throw new AppException(
                $"Čerpaná částka z fondu ({fundDrawAmount:F2} Kč) přesahuje dostupný zůstatek fondu ({fundBalance.FundBalance:F2} Kč).");
        }

        var perHouse = Math.Round(fundDrawAmount / activeHouses.Count, 2);
        var paymentDate = DateTime.SpecifyKind(period.DateTo, DateTimeKind.Utc);

        // RowKey/Id are DETERMINISTIC (derived only from period.Id), not a random
        // guid, on purpose: if CloseAsync is retried after a partial failure (e.g.
        // a crash between this batch and the final Settlement writes), re-running
        // it with the same or a corrected fundDrawAmount must overwrite these same
        // rows rather than create duplicates. This relies on Azure.Data.Tables'
        // UpsertAsync being a true upsert (same PK+RK replaces), the same guarantee
        // CloseBillingPeriodUseCase already relies on for the per-house Settlement
        // writes below.
        foreach (var house in activeHouses)
        {
            var doplatek = new AdvancePayment
            {
                HouseId = house.Id,
                Year = paymentDate.Year,
                Month = paymentDate.Month,
                Amount = perHouse,
                WaterAmount = perHouse,
                ElectricityAmount = 0m,
                CommonAmount = 0m,
                PaymentDate = paymentDate,
                Type = PaymentType.Doplatek,
                IsFundTransfer = true,
                Note = $"Použito ze společného fondu — {period.Name}",
                RowKey = $"FUND-{period.Id}",
            };
            await _advanceRepository.UpsertAsync(doplatek);
        }

        var expense = new FinancialRecord
        {
            Id = $"fund-{period.Id}",
            Year = paymentDate.Year,
            Type = FinancialRecordType.Expense,
            Category = "fond-voda",
            Amount = fundDrawAmount,
            Date = paymentDate,
            Description = $"Čerpání společného fondu pro vyúčtování vody — {period.Name}",
        };
        await _financialRecordRepository.UpsertAsync(expense);
    }

    /// <summary>
    /// Carries the period's realized invoice price/m³ (incl. loss) forward as
    /// the new recommended water-advance price.
    /// </summary>
    private async Task ApplyNewWaterPriceAsync(DTOs.SettlementPreviewResponse preview, DateTime? newWaterPriceValidFrom)
    {
        if (newWaterPriceValidFrom is null)
        {
            throw new AppException("Při aplikaci nové ceny vody je nutné zadat datum platnosti od.");
        }

        var totalWaterM3 = preview.TotalHouseConsumption + preview.TotalLoss;
        if (totalWaterM3 <= 0)
        {
            throw new AppException("Nelze odvodit novou cenu vody: celková spotřeba za období je nulová.");
        }

        var effectivePrice = Math.Round(preview.TotalInvoiceAmount / totalWaterM3, 2);

        var settings = await _advanceSettingsRepository.GetAsync();
        settings.WaterPricePerM3 = effectivePrice;
        settings.WaterPriceValidFrom = DateTime.SpecifyKind(newWaterPriceValidFrom.Value, DateTimeKind.Utc);
        await _advanceSettingsRepository.UpsertAsync(settings);
    }
}
