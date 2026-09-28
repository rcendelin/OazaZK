namespace Oaza.Application.DTOs;

// House transfer to a new owner (T03, R7). Saldo sign (X1): positive = přeplatek.

public class HouseTransferPreviewResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;

    /// <summary>Day the new owner takes over.</summary>
    public DateOnly TransferDate { get; set; }

    /// <summary>The old owner's last day = the house interim closing (transfer date − 1).</summary>
    public DateOnly ClosingDate { get; set; }

    public string? CurrentOwnerName { get; set; }
    public DateOnly? CurrentOwnerFrom { get; set; }

    /// <summary>The old owner's closing saldo at <see cref="ClosingDate"/>.</summary>
    public decimal ClosingSaldo { get; set; }

    public decimal ClosingPayments { get; set; }
    public decimal ClosingCosts { get; set; }
    public string? MeterId { get; set; }
    public string? MeterNumber { get; set; }

    /// <summary>Meter state suggested from the readings (interpolated if needed).</summary>
    public decimal? SuggestedMeterValue { get; set; }

    public string? SuggestedMeterNote { get; set; }

    /// <summary>Why the transfer cannot be done as asked (empty = it can).</summary>
    public List<string> Problems { get; set; } = [];
}

public class HouseTransferRequest
{
    public DateOnly TransferDate { get; set; }
    public string NewOwnerName { get; set; } = string.Empty;
    public string? NewOwnerContact { get; set; }

    /// <summary>Meter state at the handover, m³ (required when the house has a meter).</summary>
    public decimal? MeterValue { get; set; }

    public bool MeterIsEstimate { get; set; }
    public string? MeterSource { get; set; }

    /// <summary>The new owner's starting fund share (default 0; a starting deposit if paid). Positive = credit.</summary>
    public decimal FundShare { get; set; }

    /// <summary>Also change the house's contact person and e-mail to the new owner.</summary>
    public bool UpdateHouseContact { get; set; } = true;

    public string? Reason { get; set; }
}

public class HouseTransferResponse
{
    public string ClosingId { get; set; } = string.Empty;
    public decimal ClosingSaldo { get; set; }
    public string NewOwnershipPeriodId { get; set; } = string.Empty;
}
