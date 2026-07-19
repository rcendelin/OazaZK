namespace Oaza.Application.DTOs;

public record SettlementPreviewResponse(
    string PeriodId,
    string PeriodName,
    DateTime DateFrom,
    DateTime DateTo,
    decimal MainMeterConsumption,
    decimal TotalHouseConsumption,
    decimal TotalLoss,
    decimal TotalInvoiceAmount,
    string LossAllocationMethod,
    int MonthsInPeriod,
    decimal TotalElectricityCharge,
    decimal TotalCommonCharge,
    List<HouseSettlementDetail> Houses
);

public record HouseSettlementDetail(
    string HouseId,
    string HouseName,
    decimal ConsumptionM3,
    decimal LossAllocatedM3,
    decimal SharePercent,
    decimal CalculatedAmount,     // water charge
    decimal TotalAdvances,        // water advances + doplatky
    decimal Balance,              // water: positive = doplatek, negative = přeplatek
    decimal ElectricityCharge,
    decimal ElectricityAdvances,
    decimal CommonCharge,
    decimal CommonAdvances
);

public record SettlementResponse(
    string PeriodId,
    string HouseId,
    string HouseName,
    decimal ConsumptionM3,
    decimal SharePercent,
    decimal CalculatedAmount,
    decimal TotalAdvances,
    decimal Balance,
    decimal LossAllocatedM3,
    decimal ElectricityCharge,
    decimal ElectricityAdvances,
    decimal CommonCharge,
    decimal CommonAdvances
);

public record CalculateSettlementRequest(
    string LossAllocationMethod, // "Equal" or "ProportionalToConsumption"
    decimal FundDrawAmount = 0m,
    bool ApplyNewWaterPrice = false,
    DateTime? NewWaterPriceValidFrom = null
);
