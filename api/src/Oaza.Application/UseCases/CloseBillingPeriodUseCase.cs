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
/// </summary>
public class CloseBillingPeriodUseCase
{
    private readonly CalculateSettlementUseCase _calculateSettlementUseCase;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly ILogger<CloseBillingPeriodUseCase> _logger;

    public CloseBillingPeriodUseCase(
        CalculateSettlementUseCase calculateSettlementUseCase,
        IBillingPeriodRepository billingPeriodRepository,
        ISettlementRepository settlementRepository,
        ILogger<CloseBillingPeriodUseCase> logger)
    {
        _calculateSettlementUseCase = calculateSettlementUseCase ?? throw new ArgumentNullException(nameof(calculateSettlementUseCase));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _settlementRepository = settlementRepository ?? throw new ArgumentNullException(nameof(settlementRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates and persists settlements for the period, then locks it.
    /// Returns the persisted Settlement entities.
    /// </summary>
    public async Task<IReadOnlyList<Settlement>> CloseAsync(
        string periodId, LossAllocationMethod lossAllocationMethod)
    {
        // Calculate final settlement numbers (also validates the period is Open).
        var preview = await _calculateSettlementUseCase.CalculateAsync(periodId, lossAllocationMethod);

        // Re-verify the period is still Open before committing the irreversible close.
        var period = await _billingPeriodRepository.GetAsync(PartitionKeys.Period, periodId)
            ?? throw new NotFoundException("BillingPeriod", periodId);

        if (period.Status != BillingPeriodStatus.Open)
        {
            throw new AppException("Billing period is already closed. Cannot close again.");
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
}
