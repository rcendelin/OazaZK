using Oaza.Application.Ledger;
using Oaza.Domain.Enums;

namespace Oaza.Application.DTOs;

// House ledger and overview (T07). Saldo sign (X1): positive = přeplatek (the association owes the house),
// negative = nedoplatek. Item amounts are their effect on the saldo.

public class LedgerDetailResponse
{
    public decimal Total { get; set; }
    public DateOnly SegmentFrom { get; set; }
    public DateOnly SegmentTo { get; set; }
    public int SegmentDays { get; set; }
    public decimal SegmentAmount { get; set; }
    public AllocationMethod Method { get; set; }
    public decimal Weight { get; set; }
    public decimal TotalWeight { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public class LedgerItemResponse
{
    public DateOnly Date { get; set; }
    public LedgerItemKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ComponentId { get; set; }
    public string? ComponentName { get; set; }

    /// <summary>Effect on the saldo: payments and credits positive, costs negative.</summary>
    public decimal Amount { get; set; }

    /// <summary>Saldo after this item.</summary>
    public decimal Balance { get; set; }

    public LedgerDetailResponse? Detail { get; set; }
}

public class LedgerPeriodResponse
{
    public string Id { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

public class HouseLedgerResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>The ownership period shown (null = the house has none yet).</summary>
    public LedgerPeriodResponse? OwnershipPeriod { get; set; }

    public List<LedgerPeriodResponse> OwnershipPeriods { get; set; } = [];
    public decimal Opening { get; set; }
    public decimal Payments { get; set; }

    /// <summary>Costs net of credits (positive = the house bears them).</summary>
    public decimal Costs { get; set; }

    public decimal Saldo { get; set; }
    public List<LedgerItemResponse> Items { get; set; } = [];
}

public class LedgerOverviewHouse
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public decimal Opening { get; set; }
    public decimal Payments { get; set; }

    /// <summary>Component id → the house's costs net of credits.</summary>
    public Dictionary<string, decimal> Costs { get; set; } = [];

    public decimal Saldo { get; set; }
}

public class LedgerOverviewComponent
{
    public string ComponentId { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;

    /// <summary>What the component allocated in the range.</summary>
    public decimal AllocatedTotal { get; set; }

    /// <summary>Σ over the houses — must equal <see cref="AllocatedTotal"/> to the haléř.</summary>
    public decimal HousesTotal { get; set; }

    public bool Matches { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public class LedgerOverviewResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<LedgerOverviewComponent> Components { get; set; } = [];
    public List<LedgerOverviewHouse> Houses { get; set; } = [];
}
