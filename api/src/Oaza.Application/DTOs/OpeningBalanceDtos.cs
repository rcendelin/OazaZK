using Oaza.Domain.Enums;

namespace Oaza.Application.DTOs;

// Opening balances and ownership periods (T03). Days are DateOnly ("yyyy-MM-dd").

public class OwnershipPeriodResponse
{
    public string Id { get; set; } = string.Empty;
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

public class StartOwnershipRequest
{
    /// <summary>Accounting start (e.g. 1. 11. 2023); houses without an ownership period get one from this day.</summary>
    public DateOnly StartDate { get; set; }
}

public class StartOwnershipResponse
{
    public int Created { get; set; }
    public List<OwnershipPeriodResponse> Periods { get; set; } = [];
}

public class OpeningBalanceResponse
{
    public string Key { get; set; } = string.Empty;
    public OpeningBalanceType Type { get; set; }
    public string? HouseId { get; set; }
    public string? HouseName { get; set; }
    public string? ComponentId { get; set; }
    public string? ComponentName { get; set; }
    public string? MeterId { get; set; }
    public string? MeterNumber { get; set; }
    public string? OwnershipPeriodId { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>m³ for MeterReading; CZK for FundShare (positive = the house has a credit) and ComponentCredit (negative = credit).</summary>
    public decimal Value { get; set; }

    public bool IsEstimate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary>True when the date is fixed by an interim closing — changes need a reason and are logged as corrections.</summary>
    public bool Locked { get; set; }
}

public class SaveOpeningBalanceRequest
{
    public OpeningBalanceType Type { get; set; }

    /// <summary>FundShare: required.</summary>
    public string? HouseId { get; set; }

    /// <summary>MeterReading: required (the house is the meter's house).</summary>
    public string? MeterId { get; set; }

    /// <summary>ComponentCredit: required.</summary>
    public string? ComponentId { get; set; }

    public DateOnly Date { get; set; }
    public decimal Value { get; set; }
    public bool IsEstimate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Note { get; set; }

    /// <summary>Required when changing a value already fixed by an interim closing (a correction).</summary>
    public string? Reason { get; set; }
}

public class ComponentCreditShareResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public decimal Amount { get; set; }
}

public class ComponentCreditPreviewResponse
{
    public string ComponentId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public AllocationMethod Method { get; set; }
    public decimal Total { get; set; }
    public List<ComponentCreditShareResponse> Shares { get; set; } = [];
}
