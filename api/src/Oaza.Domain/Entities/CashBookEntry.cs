using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

/// <summary>
/// One entry of the association's cash book (T09, R10): a deposit, an expense (also without a receipt) or the storno
/// of an earlier entry. Entries are never changed or deleted — a mistake is corrected by a storno.
/// </summary>
public class CashBookEntry
{
    public string Id { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public CashBookEntryType Type { get; set; }

    /// <summary>Always positive; the direction follows from <see cref="Type"/> (a storno reverses its original).</summary>
    public decimal Amount { get; set; }

    /// <summary>E.g. údržba okolí, materiál, služby, výběr z účtu.</summary>
    public string Category { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>To or from whom — required for an expense without a receipt.</summary>
    public string? Counterparty { get; set; }

    public bool HasReceipt { get; set; }
    public string? DocumentId { get; set; }

    /// <summary>Reference of the bank movement for a deposit withdrawn from the account (manual when not imported).</summary>
    public string? BankTransactionRef { get; set; }

    /// <summary>For a storno: the entry it reverses.</summary>
    public string? CorrectionOf { get; set; }

    /// <summary>For an expense that is a shared cost: the component and the cost entry created for it (paid from cash).</summary>
    public string? ComponentId { get; set; }

    public string? CostEntryId { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}
