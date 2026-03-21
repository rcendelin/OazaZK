using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

public class CalculateSettlementUseCase
{
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IWaterMeterRepository _meterRepository;
    private readonly IMeterReadingRepository _readingRepository;
    private readonly ISupplierInvoiceRepository _invoiceRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly ILogger<CalculateSettlementUseCase> _logger;

    public CalculateSettlementUseCase(
        IBillingPeriodRepository billingPeriodRepository,
        IHouseRepository houseRepository,
        IWaterMeterRepository meterRepository,
        IMeterReadingRepository readingRepository,
        ISupplierInvoiceRepository invoiceRepository,
        IAdvancePaymentRepository advanceRepository,
        ILogger<CalculateSettlementUseCase> logger)
    {
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _meterRepository = meterRepository ?? throw new ArgumentNullException(nameof(meterRepository));
        _readingRepository = readingRepository ?? throw new ArgumentNullException(nameof(readingRepository));
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Calculates settlement preview for a billing period. Does NOT save anything.
    /// </summary>
    public async Task<SettlementPreviewResponse> CalculateAsync(
        string periodId, LossAllocationMethod lossAllocationMethod)
    {
        // 1. Load billing period, verify it is Open
        var period = await LoadAndValidatePeriodAsync(periodId);

        // 2. Load all active houses
        var allHouses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
        var activeHouses = allHouses.Where(h => h.IsActive).ToList();

        if (activeHouses.Count == 0)
        {
            throw new AppException("No active houses found for settlement calculation.");
        }

        // 3. Load all water meters
        var allMeters = await _meterRepository.GetByPartitionKeyAsync(PartitionKeys.Meter);
        var mainMeter = allMeters.FirstOrDefault(m => m.Type == MeterType.Main);

        if (mainMeter is null)
        {
            throw new AppException("No main water meter found.");
        }

        // 4. Get main meter consumption for the period
        var mainConsumption = await GetMeterConsumptionAsync(
            mainMeter.Id, period.DateFrom, period.DateTo, "Main meter");

        // 5–6. Calculate each house's consumption
        var houseConsumptions = new Dictionary<string, decimal>();
        foreach (var house in activeHouses)
        {
            var houseMeter = allMeters.FirstOrDefault(
                m => m.Type == MeterType.Individual && m.HouseId == house.Id);

            if (houseMeter is null)
            {
                _logger.LogWarning(
                    "House {HouseId} ({HouseName}) has no individual meter — skipping.",
                    house.Id, house.Name);
                continue;
            }

            try
            {
                var consumption = await GetMeterConsumptionAsync(
                    houseMeter.Id, period.DateFrom, period.DateTo,
                    $"House meter for {house.Name}");
                houseConsumptions[house.Id] = consumption;
            }
            catch (AppException ex)
            {
                _logger.LogWarning(
                    "Skipping house {HouseId} ({HouseName}) due to missing readings: {Message}",
                    house.Id, house.Name, ex.Message);
            }
        }

        if (houseConsumptions.Count == 0)
        {
            throw new AppException("No house meter readings found for the period. Cannot calculate settlement.");
        }

        var totalHouseConsumption = houseConsumptions.Values.Sum();

        // 7. Calculate loss (clamp to 0 if negative — meter error)
        var loss = mainConsumption - totalHouseConsumption;
        if (loss < 0)
        {
            _logger.LogWarning(
                "Negative loss detected ({Loss} m³). Individual meters sum exceeds main meter. Treating loss as 0.",
                loss);
            loss = 0;
        }

        // 8. Allocate loss to each house
        var lossAllocations = AllocateLoss(
            loss, houseConsumptions, totalHouseConsumption, lossAllocationMethod);

        // 9. Load supplier invoices for the period
        var invoices = await _invoiceRepository.GetByPeriodAsync(period.DateFrom, period.DateTo);
        var totalInvoiceAmount = invoices.Sum(i => i.Amount);

        // 10–12. Calculate each house's share, amount, advances, and balance
        var housesWithMeters = activeHouses
            .Where(h => houseConsumptions.ContainsKey(h.Id))
            .ToList();

        var totalConsumptionPlusLoss = totalHouseConsumption + loss;

        var houseDetails = new List<HouseSettlementDetail>();
        foreach (var house in housesWithMeters)
        {
            var consumption = houseConsumptions[house.Id];
            var allocatedLoss = lossAllocations[house.Id];

            decimal sharePercent;
            if (totalConsumptionPlusLoss > 0)
            {
                sharePercent = (consumption + allocatedLoss) / totalConsumptionPlusLoss * 100m;
            }
            else
            {
                // If total consumption is 0, split equally
                sharePercent = 100m / housesWithMeters.Count;
            }

            var calculatedAmount = Math.Round(sharePercent / 100m * totalInvoiceAmount, 2);

            // 11. Load advance payments for the house within period dates
            var advances = await _advanceRepository.GetByHouseAndPeriodAsync(
                house.Id, period.DateFrom, period.DateTo);
            var totalAdvances = advances.Sum(a => a.Amount);

            // 12. Balance: positive = underpayment (doplatek), negative = overpayment (přeplatek)
            var balance = Math.Round(calculatedAmount - totalAdvances, 2);

            houseDetails.Add(new HouseSettlementDetail(
                HouseId: house.Id,
                HouseName: house.Name,
                ConsumptionM3: Math.Round(consumption, 3),
                LossAllocatedM3: Math.Round(allocatedLoss, 3),
                SharePercent: Math.Round(sharePercent, 2),
                CalculatedAmount: calculatedAmount,
                TotalAdvances: Math.Round(totalAdvances, 2),
                Balance: balance
            ));
        }

        return new SettlementPreviewResponse(
            PeriodId: period.Id,
            PeriodName: period.Name,
            DateFrom: period.DateFrom,
            DateTo: period.DateTo,
            MainMeterConsumption: Math.Round(mainConsumption, 3),
            TotalHouseConsumption: Math.Round(totalHouseConsumption, 3),
            TotalLoss: Math.Round(loss, 3),
            TotalInvoiceAmount: Math.Round(totalInvoiceAmount, 2),
            LossAllocationMethod: lossAllocationMethod.ToString(),
            Houses: houseDetails
        );
    }

    /// <summary>
    /// Loads and validates a billing period, ensuring it exists and is Open.
    /// </summary>
    internal async Task<BillingPeriod> LoadAndValidatePeriodAsync(string periodId)
    {
        var period = await _billingPeriodRepository.GetAsync(PartitionKeys.Period, periodId);
        if (period is null)
        {
            throw new NotFoundException("BillingPeriod", periodId);
        }

        if (period.Status != BillingPeriodStatus.Open)
        {
            throw new AppException("Billing period is already closed. Settlements cannot be recalculated.");
        }

        return period;
    }

    /// <summary>
    /// Gets the consumption for a meter over a date range by finding the closest readings
    /// to the period boundaries.
    /// </summary>
    private async Task<decimal> GetMeterConsumptionAsync(
        string meterId, DateTime periodStart, DateTime periodEnd, string meterDescription)
    {
        var allReadings = await _readingRepository.GetByMeterIdAsync(meterId);
        if (allReadings.Count == 0)
        {
            throw new AppException($"No readings found for {meterDescription} (meter ID: {meterId}).");
        }

        // Sort readings by date ascending
        var sortedReadings = allReadings.OrderBy(r => r.ReadingDate).ToList();

        // Find the reading closest to (but not after) periodStart
        var startReading = sortedReadings
            .Where(r => r.ReadingDate <= periodStart)
            .OrderByDescending(r => r.ReadingDate)
            .FirstOrDefault();

        // If no reading exists before the start date, use the earliest available
        startReading ??= sortedReadings.First();

        // Find the reading closest to (but not after) periodEnd
        var endReading = sortedReadings
            .Where(r => r.ReadingDate <= periodEnd)
            .OrderByDescending(r => r.ReadingDate)
            .FirstOrDefault();

        if (endReading is null)
        {
            throw new AppException(
                $"No reading found at or before the period end date for {meterDescription} (meter ID: {meterId}).");
        }

        // Ensure we have two distinct readings
        if (startReading.ReadingDate == endReading.ReadingDate &&
            startReading.Value == endReading.Value &&
            sortedReadings.Count > 1)
        {
            // The start and end readings are the same — no consumption can be computed
            _logger.LogWarning(
                "Start and end readings are identical for {MeterDescription}. Consumption will be 0.",
                meterDescription);
        }

        var consumption = endReading.Value - startReading.Value;
        if (consumption < 0)
        {
            throw new AppException(
                $"Negative consumption detected for {meterDescription}: end reading ({endReading.Value}) " +
                $"is less than start reading ({startReading.Value}). Check meter readings.");
        }

        return consumption;
    }

    /// <summary>
    /// Allocates water loss to houses based on the chosen method.
    /// </summary>
    private static Dictionary<string, decimal> AllocateLoss(
        decimal totalLoss,
        Dictionary<string, decimal> houseConsumptions,
        decimal totalHouseConsumption,
        LossAllocationMethod method)
    {
        var allocations = new Dictionary<string, decimal>();

        if (totalLoss == 0)
        {
            foreach (var houseId in houseConsumptions.Keys)
            {
                allocations[houseId] = 0;
            }
            return allocations;
        }

        switch (method)
        {
            case LossAllocationMethod.Equal:
            {
                var lossPerHouse = totalLoss / houseConsumptions.Count;
                foreach (var houseId in houseConsumptions.Keys)
                {
                    allocations[houseId] = lossPerHouse;
                }
                break;
            }

            case LossAllocationMethod.ProportionalToConsumption:
            {
                if (totalHouseConsumption == 0)
                {
                    // Cannot allocate proportionally with zero consumption — fall back to equal
                    var lossPerHouse = totalLoss / houseConsumptions.Count;
                    foreach (var houseId in houseConsumptions.Keys)
                    {
                        allocations[houseId] = lossPerHouse;
                    }
                }
                else
                {
                    foreach (var (houseId, consumption) in houseConsumptions)
                    {
                        allocations[houseId] = totalLoss * (consumption / totalHouseConsumption);
                    }
                }
                break;
            }

            default:
                throw new AppException($"Unknown loss allocation method: {method}");
        }

        return allocations;
    }
}
