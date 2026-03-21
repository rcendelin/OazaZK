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
    List<HouseSettlementDetail> Houses
);

public record HouseSettlementDetail(
    string HouseId,
    string HouseName,
    decimal ConsumptionM3,
    decimal LossAllocatedM3,
    decimal SharePercent,
    decimal CalculatedAmount,
    decimal TotalAdvances,
    decimal Balance // positive = underpayment/doplatek, negative = overpayment/přeplatek
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
    decimal LossAllocatedM3
);

public record CalculateSettlementRequest(
    string LossAllocationMethod // "Equal" or "ProportionalToConsumption"
);
