namespace Oaza.Domain.Entities;

/// <summary>
/// A span in which a house belongs to one owner (T03, R7). A sale ends the period and a new one
/// starts with its own opening values; the new owner does not inherit the history.
/// </summary>
public class OwnershipPeriod
{
    /// <summary>Deterministic id <c>{HouseId}|{ValidFrom:yyyy-MM-dd}</c> — one period per house per start day.</summary>
    public string Id { get; set; } = string.Empty;

    public string HouseId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public DateOnly ValidFrom { get; set; }

    /// <summary>Last day of ownership (inclusive); null = current owner.</summary>
    public DateOnly? ValidTo { get; set; }

    public static string KeyFor(string houseId, DateOnly validFrom) => $"{houseId}|{validFrom:yyyy-MM-dd}";
}
