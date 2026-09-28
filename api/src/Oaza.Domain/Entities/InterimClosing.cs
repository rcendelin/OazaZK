using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

/// <summary>
/// A cut at a date that fixes the saldo (T08): days up to and including <see cref="Date"/> can no longer change;
/// corrections are booked into the open period. Keeps a snapshot of the saldo of every house it covers.
/// </summary>
public class InterimClosing
{
    /// <summary>Deterministic id <c>{Date:yyyy-MM-dd}|{Scope}|{HouseId or -}</c> — one closing per day, scope and house.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Last closed day (inclusive).</summary>
    public DateOnly Date { get; set; }

    public ClosingScope Scope { get; set; }
    public string? HouseId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>JSON array of the houses' saldo at <see cref="Date"/> (see <c>ClosingSnapshotRow</c>).</summary>
    public string SnapshotJson { get; set; } = "[]";

    public static string KeyFor(DateOnly date, ClosingScope scope, string? houseId) => $"{date:yyyy-MM-dd}|{scope}|{houseId ?? "-"}";
}
