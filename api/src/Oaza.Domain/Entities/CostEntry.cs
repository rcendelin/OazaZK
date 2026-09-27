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

    /// <summary>
    /// Set when the entry reaches into a period already fixed by an interim closing (T08): its shares are split by the
    /// original segments (who took part when), but booked into the saldo on this first open day — a correction.
    /// </summary>
    public DateOnly? PostingDate { get; set; }

    /// <summary>The entry this one corrects, if any.</summary>
    public string? CorrectionOf { get; set; }

    /// <summary>Natural key from the seed import (T13) — the <c>ref</c> column; unique among all entries. Optional.</summary>
    public string? ExternalRef { get; set; }

    /// <summary>The day that decides whether the entry is closed: the posting day, else the period start.</summary>
    public DateOnly LockDate => PostingDate ?? PeriodFrom;
}
