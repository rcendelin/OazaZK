using Oaza.Domain.Enums;

namespace Oaza.Application.DTOs;

// Interim closings (T08). Saldo sign (X1): positive = přeplatek.

public class ClosingSnapshotRow
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public decimal Opening { get; set; }
    public decimal Payments { get; set; }
    public decimal Costs { get; set; }
    public decimal Saldo { get; set; }
}

public class ClosingHouseResponse : ClosingSnapshotRow
{
    /// <summary>Saldo at the closing date recomputed now.</summary>
    public decimal CurrentSaldo { get; set; }

    /// <summary>Current − snapshot; non-zero means something changed in the closed period.</summary>
    public decimal Difference { get; set; }
}

public class InterimClosingResponse
{
    public string Id { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public ClosingScope Scope { get; set; }
    public string? HouseId { get; set; }
    public string? HouseName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalSaldo { get; set; }

    /// <summary>Only the latest closing can be removed (a mistake); older ones are fixed.</summary>
    public bool CanDelete { get; set; }

    /// <summary>Filled in the detail: snapshot vs. current state.</summary>
    public List<ClosingHouseResponse>? Houses { get; set; }
}

public class CreateInterimClosingRequest
{
    /// <summary>Last closed day (inclusive), e.g. 31. 12. for the annual closing.</summary>
    public DateOnly Date { get; set; }

    public ClosingScope Scope { get; set; }
    public string? HouseId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
