using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

/// <summary>
/// A starting value at a date (T03): meter state, the house's fund share, or a component's credit
/// with the supplier. At most one per (type, target, ownership period) — the <see cref="Key"/> says so.
/// </summary>
public class OpeningBalance
{
    /// <summary>Natural key <c>{Type}|{target id}|{ownership period id or -}</c>; see <see cref="KeyFor"/>.</summary>
    public string Key { get; set; } = string.Empty;

    public OpeningBalanceType Type { get; set; }

    /// <summary>House (MeterReading: the meter's house; FundShare: required; ComponentCredit: null).</summary>
    public string? HouseId { get; set; }

    public string? ComponentId { get; set; }
    public string? MeterId { get; set; }
    public string? OwnershipPeriodId { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>m³ for MeterReading, CZK for FundShare and ComponentCredit (signs: see <see cref="OpeningBalanceType"/>).</summary>
    public decimal Value { get; set; }

    public bool IsEstimate { get; set; }

    /// <summary>Where the value comes from — required (e.g. „odečet 22. 5. 2023, foto Jindra“).</summary>
    public string Source { get; set; } = string.Empty;

    public string? Note { get; set; }

    public static string KeyFor(OpeningBalanceType type, string targetId, string? ownershipPeriodId) =>
        $"{type}|{targetId}|{ownershipPeriodId ?? "-"}";
}
