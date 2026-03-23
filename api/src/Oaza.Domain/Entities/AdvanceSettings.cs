namespace Oaza.Domain.Entities;

/// <summary>
/// Global configuration for advance payment calculation.
/// Stored as a single record in Table Storage (PK="SETTINGS", RK="advances").
/// </summary>
public class AdvanceSettings
{
    // ── Water ──
    /// <summary>Water price per m³ in CZK.</summary>
    public decimal WaterPricePerM3 { get; set; }

    /// <summary>Start date of the current water price validity.</summary>
    public DateTime WaterPriceValidFrom { get; set; }

    /// <summary>End date of the current water price validity (null = open-ended).</summary>
    public DateTime? WaterPriceValidTo { get; set; }

    // ── Electricity (well pump) ──
    /// <summary>Monthly electricity cost for the well pump in CZK (total, to be split).</summary>
    public decimal MonthlyElectricityCost { get; set; }

    /// <summary>
    /// Electricity cost distribution coefficients per house.
    /// Key = houseId, Value = percentage (all should sum to 100).
    /// </summary>
    public Dictionary<string, decimal> ElectricityCoefficients { get; set; } = new();

    // ── Common base ──
    /// <summary>Monthly common base fee per house (maintenance, insurance, admin) in CZK.</summary>
    public decimal MonthlyCommonBaseFee { get; set; }

    // ── Per-house overrides ──
    /// <summary>
    /// Actual monthly advance set per house (admin override).
    /// Key = houseId, Value = { waterAdvance, electricityAdvance, commonAdvance }
    /// Stored as JSON in Table Storage.
    /// </summary>
    public Dictionary<string, HouseAdvanceOverride> HouseOverrides { get; set; } = new();

    /// <summary>
    /// Loss allocation method: "Equal" or "ProportionalToConsumption"
    /// </summary>
    public string LossAllocationMethod { get; set; } = "ProportionalToConsumption";
}

/// <summary>
/// Per-house advance payment override (actual amounts set by admin).
/// </summary>
public class HouseAdvanceOverride
{
    public decimal WaterAdvance { get; set; }
    public decimal ElectricityAdvance { get; set; }
    public decimal CommonAdvance { get; set; }
}
