namespace Oaza.Domain.Enums;

/// <summary>Kind of a cash book entry (T09).</summary>
public enum CashBookEntryType
{
    /// <summary>Cash in — typically a withdrawal from the association's bank account.</summary>
    Deposit,

    /// <summary>Cash out — with or without a receipt (R10).</summary>
    Expense,

    /// <summary>Storno of an earlier entry: reverses it in full (entries are never deleted).</summary>
    Correction,
}
