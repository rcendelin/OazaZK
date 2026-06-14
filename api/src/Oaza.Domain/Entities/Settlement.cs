namespace Oaza.Domain.Entities;

public class Settlement
{
    public string PeriodId { get; set; } = string.Empty;
    public string HouseId { get; set; } = string.Empty;
    public decimal ConsumptionM3 { get; set; }
    public decimal SharePercent { get; set; }

    // ── Water (the invoiced settlement) ──
    /// <summary>Water charge for the period (CZK) = share × supplier-invoice total.</summary>
    public decimal CalculatedAmount { get; set; }

    /// <summary>Water advances + doplatky paid in the period (CZK).</summary>
    public decimal TotalAdvances { get; set; }

    /// <summary>Water balance = CalculatedAmount − TotalAdvances (positive = doplatek).</summary>
    public decimal Balance { get; set; }

    public decimal LossAllocatedM3 { get; set; }

    // ── Electricity (pump) — budget-based accrual over the period ──
    /// <summary>Electricity charge = monthly cost × house coefficient × months in period.</summary>
    public decimal ElectricityCharge { get; set; }

    /// <summary>Electricity advances + doplatky paid in the period (CZK).</summary>
    public decimal ElectricityAdvances { get; set; }

    // ── Common base — budget-based accrual over the period ──
    /// <summary>Common-base charge = monthly fee × months in period.</summary>
    public decimal CommonCharge { get; set; }

    /// <summary>Common-base advances + doplatky paid in the period (CZK).</summary>
    public decimal CommonAdvances { get; set; }
}
