using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

/// <summary>
/// A cost of a component for a period (T06): a supplier advance, a supplier settlement or a one-off cost.
/// It is allocated to houses by the component's rules and participation over <see cref="PeriodFrom"/>–<see cref="PeriodTo"/>;
/// how it was paid (<see cref="PaidFrom"/>) does not matter for the allocation (R6).
/// For a metered component (water PVK) entries are the supplier invoices; their <see cref="QuantityM3"/>
/// gives the price per m³ used for the metered allocation and losses (T05).
/// </summary>
public class CostEntry
{
    public string Id { get; set; } = string.Empty;
    public string ComponentId { get; set; } = string.Empty;
    public CostEntryType Type { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }

    /// <summary>CZK incl. VAT, 2 decimals; a settlement may be negative (overpayment).</summary>
    public decimal Amount { get; set; }

    /// <summary>Invoiced quantity in m³ (metered components); null otherwise.</summary>
    public decimal? QuantityM3 { get; set; }

    public string? Supplier { get; set; }

    /// <summary>Linked PDF in Documents (T11).</summary>
    public string? DocumentId { get; set; }

    public PaidFrom PaidFrom { get; set; }
    public string? Note { get; set; }
}
