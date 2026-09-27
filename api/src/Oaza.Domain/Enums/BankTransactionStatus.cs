namespace Oaza.Domain.Enums;

public enum BankTransactionStatus
{
    /// <summary>Recorded as an advance/doplatek payment.</summary>
    Imported,

    /// <summary>Explicitly skipped by the admin (not a household payment).</summary>
    Ignored,
}
