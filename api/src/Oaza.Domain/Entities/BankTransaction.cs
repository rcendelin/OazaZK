using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

/// <summary>
/// A bank statement movement that has already been processed by the bank import
/// (imported as a payment or explicitly ignored). Keyed by the bank's own
/// operation id so re-uploading a statement never duplicates payments.
/// </summary>
public class BankTransaction
{
    /// <summary>Normalized key of the association's own account (PartitionKey).</summary>
    public string OwnAccountKey { get; set; } = string.Empty;

    /// <summary>Bank operation id ("ID operace"), RowKey.</summary>
    public string TransactionId { get; set; } = string.Empty;

    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string? CounterAccount { get; set; }
    public string? CounterName { get; set; }
    public string? Message { get; set; }
    public string? VariableSymbol { get; set; }

    public BankTransactionStatus Status { get; set; }

    /// <summary>House and payment RowKey of the created <see cref="AdvancePayment"/> (Imported only).</summary>
    public string? HouseId { get; set; }
    public string? PaymentRowKey { get; set; }

    public DateTime ImportedAt { get; set; }
    public string ImportedBy { get; set; } = string.Empty;
}
