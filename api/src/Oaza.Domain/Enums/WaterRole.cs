namespace Oaza.Domain.Enums;

/// <summary>Role of a metered component in the water settlement (T05).</summary>
public enum WaterRole
{
    None,

    /// <summary>Water PVK — house consumption × price; its cost entries are the PVK invoices.</summary>
    Consumption,

    /// <summary>Water losses — main meter − Σ house meters, allocated by the component's rule (O1).</summary>
    Losses,
}
