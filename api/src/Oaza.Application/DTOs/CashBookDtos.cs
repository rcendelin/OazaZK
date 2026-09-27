using Oaza.Domain.Enums;

namespace Oaza.Application.DTOs;

// Cash book (T09). Days are DateOnly ("yyyy-MM-dd").

public class CashBookEntryResponse
{
    public string Id { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public CashBookEntryType Type { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Effect on the balance: deposits positive, expenses negative, a storno the opposite of its original.</summary>
    public decimal Effect { get; set; }

    public decimal Balance { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Counterparty { get; set; }
    public bool HasReceipt { get; set; }
    public string? DocumentId { get; set; }
    public string? BankTransactionRef { get; set; }
    public string? CorrectionOf { get; set; }

    /// <summary>Id of the storno that reversed this entry, if any.</summary>
    public string? CorrectedBy { get; set; }

    public string? ComponentId { get; set; }
    public string? ComponentName { get; set; }
    public string? CostEntryId { get; set; }
    public string? CreatedByName { get; set; }
}

public class CashBookResponse
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    /// <summary>Balance before <see cref="From"/>.</summary>
    public decimal OpeningBalance { get; set; }

    public decimal Deposits { get; set; }
    public decimal Expenses { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<CashBookEntryResponse> Entries { get; set; } = [];
}

public class CreateCashBookEntryRequest
{
    public DateOnly Date { get; set; }

    /// <summary>Deposit or Expense (a storno goes through its own endpoint).</summary>
    public CashBookEntryType Type { get; set; }

    public decimal Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Counterparty { get; set; }
    public bool HasReceipt { get; set; }
    public string? DocumentId { get; set; }
    public string? BankTransactionRef { get; set; }

    /// <summary>For an expense that is a shared cost: the component — a one-off cost entry paid from cash is created.</summary>
    public string? ComponentId { get; set; }
}

public class StornoCashBookEntryRequest
{
    public string Reason { get; set; } = string.Empty;
}
