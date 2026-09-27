namespace Oaza.Domain.Entities;

/// <summary>
/// Per-house advance overrides set by the admin (X2: the old pricing — water price, electricity cost and
/// coefficients, common base fee, loss method — was removed; the recommendation now comes from the ledger).
/// Stored as a single record in Table Storage (PK="SETTINGS", RK="advances").
/// </summary>
public class AdvanceSettings
{
    /// <summary>
    /// Actual monthly advance set per house (admin override).
    /// Key = houseId, Value = { waterAdvance, electricityAdvance, commonAdvance }
    /// Stored as JSON in Table Storage.
    /// </summary>
    public Dictionary<string, HouseAdvanceOverride> HouseOverrides { get; set; } = new();
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
