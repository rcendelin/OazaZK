using Oaza.Domain.Enums;

namespace Oaza.Application.OffBookFunds;

// Off-book fund (T10). Never part of the association's accounts — see OffBookFundUseCase.

public class OffBookFundResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string ManagerName { get; set; } = string.Empty;
    public string? AccountDescription { get; set; }
    public bool Active { get; set; }

    /// <summary>Contributions − expenses paid from the fund − settlements.</summary>
    public decimal Balance { get; set; }

    /// <summary>What the fund still owes to those who paid expenses in advance.</summary>
    public decimal Outstanding { get; set; }
}

public class FundCallHouseResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public decimal Expected { get; set; }
    public decimal Paid { get; set; }
    public bool IsPaid { get; set; }
}

public class FundCallResponse
{
    public string Id { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal AmountPerHouse { get; set; }
    public string Text { get; set; } = string.Empty;
    public decimal Collected { get; set; }
    public int Debtors { get; set; }
    public List<FundCallHouseResponse> Houses { get; set; } = [];
}

public class FundRecordResponse
{
    public string Id { get; set; } = string.Empty;
    public FundRecordKind Kind { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? HouseName { get; set; }
    public string? CallId { get; set; }
    public string? Method { get; set; }
    public string? PaidBy { get; set; }
    public bool HasReceipt { get; set; }
    public string? ExpenseId { get; set; }
    public string? PaidTo { get; set; }
}

public class OffBookFundDetailResponse
{
    public OffBookFundResponse Fund { get; set; } = new();
    public List<FundCallResponse> Calls { get; set; } = [];

    /// <summary>Contributions, expenses and settlements, newest first.</summary>
    public List<FundRecordResponse> Records { get; set; } = [];
}

public class SaveOffBookFundRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string ManagerName { get; set; } = string.Empty;
    public string? AccountDescription { get; set; }
    public bool Active { get; set; } = true;
}

public class AddFundRecordRequest
{
    public FundRecordKind Kind { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string? Text { get; set; }
    public DateOnly? DueDate { get; set; }
    public List<string> HouseIds { get; set; } = [];
    public string? HouseId { get; set; }
    public string? CallId { get; set; }
    public string? Method { get; set; }
    public string? PaidBy { get; set; }
    public bool HasReceipt { get; set; }
    public string? ExpenseId { get; set; }
    public string? PaidTo { get; set; }
}
