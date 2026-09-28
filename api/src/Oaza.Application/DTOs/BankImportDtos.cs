namespace Oaza.Application.DTOs;

// ───────────────────── Preview ─────────────────────

public class BankImportPreviewResponse
{
    public BankStatementSummary Statement { get; set; } = new();
    public List<BankImportRow> Rows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    /// <summary>Active houses with their prescribed monthly advance — lets the UI re-suggest type/split when the admin picks a house.</summary>
    public List<BankImportHouse> Houses { get; set; } = new();

    /// <summary>houseId → months ("YYYY-MM") that already have a regular advance in storage.</summary>
    public Dictionary<string, List<string>> ExistingAdvanceMonths { get; set; } = new();
}

public class BankStatementSummary
{
    public string Account { get; set; } = string.Empty;
    public string? Number { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal? ClosingBalance { get; set; }
    public decimal? TotalIncome { get; set; }
    public decimal? TotalExpense { get; set; }
    public bool SumCheckOk { get; set; }
}

public class BankImportHouse
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public decimal WaterAmount { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal CommonAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

public static class BankImportRowStatus
{
    public const string New = "New";
    public const string AlreadyImported = "AlreadyImported";
    public const string Ignored = "Ignored";
    public const string Outgoing = "Outgoing";
    public const string UnsupportedCurrency = "UnsupportedCurrency";
}

public static class BankImportMatchSource
{
    public const string Account = "Account";
    public const string None = "None";
}

public static class BankImportAction
{
    public const string Import = "Import";
    public const string Ignore = "Ignore";
}

public class BankImportRow
{
    public string TransactionId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CZK";
    public string? CounterAccount { get; set; }
    public string? CounterName { get; set; }
    public string? Message { get; set; }
    public string? Note { get; set; }
    public string? VariableSymbol { get; set; }

    /// <summary>Bank's own operation type text ("Bezhotovostní příjem"), display only.</summary>
    public string? BankOperationType { get; set; }

    /// <summary>See <see cref="BankImportRowStatus"/>.</summary>
    public string Status { get; set; } = BankImportRowStatus.New;

    public string? HouseId { get; set; }

    /// <summary>See <see cref="BankImportMatchSource"/>.</summary>
    public string MatchSource { get; set; } = BankImportMatchSource.None;

    /// <summary>"Advance" or "Doplatek".</summary>
    public string PaymentType { get; set; } = "Doplatek";
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal WaterAmount { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal CommonAmount { get; set; }
    public List<string> Warnings { get; set; } = new();
}

// ───────────────────── Confirm ─────────────────────

/// <summary>
/// Final admin decisions for a previewed statement. Stateless on purpose: the
/// client sends every row it wants processed and the server re-validates it
/// (an in-memory preview cache is unreliable on the Consumption plan).
/// </summary>
public class ConfirmBankImportRequest
{
    /// <summary>Own account in display form ("2601634649/2010").</summary>
    public string Account { get; set; } = string.Empty;
    public List<ConfirmBankImportRow> Rows { get; set; } = new();
}

public class ConfirmBankImportRow
{
    public string TransactionId { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string? CounterAccount { get; set; }
    public string? CounterName { get; set; }
    public string? Message { get; set; }
    public string? VariableSymbol { get; set; }

    /// <summary>See <see cref="BankImportAction"/>.</summary>
    public string Action { get; set; } = BankImportAction.Import;

    public string? HouseId { get; set; }

    /// <summary>"Advance" or "Doplatek".</summary>
    public string PaymentType { get; set; } = "Doplatek";
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal WaterAmount { get; set; }
    public decimal ElectricityAmount { get; set; }
    public decimal CommonAmount { get; set; }
}

public class ConfirmBankImportResponse
{
    public int Imported { get; set; }
    public int Ignored { get; set; }
    public int NewAccounts { get; set; }

    /// <summary>Transaction ids that were already processed earlier and were left untouched.</summary>
    public List<string> Skipped { get; set; } = new();
}

// ───────────────────── House bank accounts ─────────────────────

public class BankAccountResponse
{
    public string AccountKey { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string HouseId { get; set; } = string.Empty;
    public string? AccountName { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateBankAccountRequest
{
    public string HouseId { get; set; } = string.Empty;

    /// <summary>"[prefix-]number/bankCode".</summary>
    public string AccountNumber { get; set; } = string.Empty;
    public string? AccountName { get; set; }
}
