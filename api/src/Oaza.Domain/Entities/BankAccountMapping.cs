namespace Oaza.Domain.Entities;

/// <summary>
/// A household's bank account: incoming bank payments from this counter-account
/// are assigned to <see cref="HouseId"/>. A house may have many accounts; an
/// account belongs to exactly one house.
/// </summary>
public class BankAccountMapping
{
    /// <summary>Normalized account key (see <c>BankAccountNumber.ToKey</c>), used as RowKey.</summary>
    public string AccountKey { get; set; } = string.Empty;

    public string HouseId { get; set; } = string.Empty;

    /// <summary>Last known account holder name from the bank statement (display only).</summary>
    public string? AccountName { get; set; }

    public DateTime UpdatedAt { get; set; }
}
