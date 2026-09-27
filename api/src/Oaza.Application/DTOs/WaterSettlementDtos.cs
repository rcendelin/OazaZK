using Oaza.Domain.Enums;

namespace Oaza.Application.DTOs;

// Water PVK and losses overview (T05). Days are DateOnly ("yyyy-MM-dd").

public class WaterHouseResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public decimal ConsumptionM3 { get; set; }
    public bool IsEstimate { get; set; }
    public decimal WaterCost { get; set; }
    public decimal LossCost { get; set; }
}

public class LossSegmentResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Days { get; set; }
    public AllocationMethod Method { get; set; }
    public decimal Amount { get; set; }
    public int Participants { get; set; }
}

public class WaterIntervalResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Days { get; set; }
    public decimal MainConsumptionM3 { get; set; }
    public decimal HousesConsumptionM3 { get; set; }
    public decimal LossM3 { get; set; }
    public decimal? PricePerM3 { get; set; }
    public decimal InvoicedAmount { get; set; }
    public decimal InvoicedM3 { get; set; }
    public decimal WaterCost { get; set; }
    public decimal LossCost { get; set; }

    /// <summary>Invoiced − (water + losses); rounding, or the main meter differing from the invoiced m³.</summary>
    public decimal Difference { get; set; }

    public bool Allocated { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<LossSegmentResponse> LossSegments { get; set; } = [];
    public List<WaterHouseResponse> Houses { get; set; } = [];
}

public class WaterSettlementResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string? ConsumptionComponentName { get; set; }
    public string? LossComponentName { get; set; }
    public List<WaterIntervalResponse> Intervals { get; set; } = [];

    /// <summary>Per house over all allocated intervals.</summary>
    public List<WaterHouseResponse> Totals { get; set; } = [];
}
