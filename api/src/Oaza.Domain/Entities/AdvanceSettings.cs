namespace Oaza.Domain.Entities;

/// <summary>
/// Configuration for advance payment calculation.
/// Stored as a single record in Table Storage (PK="SETTINGS", RK="advances").
/// </summary>
public class AdvanceSettings
{
    /// <summary>Water price per m³ in CZK.</summary>
    public decimal WaterPricePerM3 { get; set; }

    /// <summary>Start date of the current water price validity.</summary>
    public DateTime WaterPriceValidFrom { get; set; }

    /// <summary>End date of the current water price validity (null = open-ended).</summary>
    public DateTime? WaterPriceValidTo { get; set; }

    /// <summary>Monthly fixed payment to the association (per house) in CZK.</summary>
    public decimal MonthlyAssociationFee { get; set; }

    /// <summary>Monthly electricity cost for the well pump in CZK (total, to be split).</summary>
    public decimal MonthlyElectricityCost { get; set; }

    /// <summary>
    /// Electricity cost distribution coefficients per house.
    /// Key = houseId, Value = percentage (all should sum to 100).
    /// </summary>
    public Dictionary<string, decimal> ElectricityCoefficients { get; set; } = new();

    /// <summary>
    /// Loss allocation method for water loss between main and individual meters.
    /// "Equal" or "ProportionalToConsumption"
    /// </summary>
    public string LossAllocationMethod { get; set; } = "ProportionalToConsumption";
}
