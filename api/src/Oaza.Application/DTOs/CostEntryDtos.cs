using Oaza.Domain.Enums;
using Oaza.Domain.Services;

namespace Oaza.Application.DTOs;

// Cost entries of components (T06). Days are DateOnly ("yyyy-MM-dd").

public class CostEntryResponse
{
    public string Id { get; set; } = string.Empty;
    public string ComponentId { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public CostEntryType Type { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public int Days { get; set; }
    public decimal Amount { get; set; }
    public decimal? QuantityM3 { get; set; }
    public string? Supplier { get; set; }
    public string? DocumentId { get; set; }
    public PaidFrom PaidFrom { get; set; }
    public string? Note { get; set; }

    /// <summary>Booked on this day because the period reaches into a closed period (a correction, T08).</summary>
    public DateOnly? PostingDate { get; set; }

    public string? CorrectionOf { get; set; }

    /// <summary>The entry is fixed by an interim closing (its posting day or period start is closed).</summary>
    public bool Locked { get; set; }
}

public class SaveCostEntryRequest
{
    public CostEntryType Type { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Required for a metered component (water PVK invoice), m³.</summary>
    public decimal? QuantityM3 { get; set; }

    public string? Supplier { get; set; }
    public string? DocumentId { get; set; }
    public PaidFrom PaidFrom { get; set; }
    public string? Note { get; set; }

    /// <summary>Required when the period reaches into a closed period (the entry becomes a correction).</summary>
    public string? Reason { get; set; }

    /// <summary>The entry this one corrects (optional link).</summary>
    public string? CorrectionOf { get; set; }

    /// <summary>Natural key from the seed import (T13); set only on create, unique among all entries.</summary>
    public string? ExternalRef { get; set; }
}

public class RecurringAdvanceRequest
{
    /// <summary>Amount of each advance, CZK.</summary>
    public decimal Amount { get; set; }

    public AdvancePeriodicity Periodicity { get; set; } = AdvancePeriodicity.Monthly;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public string? Supplier { get; set; }
    public PaidFrom PaidFrom { get; set; }
    public string? Note { get; set; }
}

public class CostShareResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal Amount { get; set; }
}

public class CostSegmentResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Days { get; set; }
    public AllocationMethod Method { get; set; }

    /// <summary>Part of the entry's amount in this segment (pro-rata by days).</summary>
    public decimal Amount { get; set; }

    public List<CostShareResponse> Shares { get; set; } = [];
}

public class CostEntryAllocationResponse
{
    public CostEntryResponse Entry { get; set; } = new();
    public List<CostSegmentResponse> Segments { get; set; } = [];

    /// <summary>Sum per house over all segments; Σ = the entry's amount.</summary>
    public List<CostShareResponse> HouseTotals { get; set; } = [];
}
