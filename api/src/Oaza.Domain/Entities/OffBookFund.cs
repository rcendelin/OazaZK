using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

/// <summary>
/// An informal fund outside the association's accounting (T10, O4), e.g. „Fond na ohňostroje“. The money is on the
/// manager's private account — the portal only keeps the evidence, strictly apart from the association's books.
/// </summary>
public class OffBookFund
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string ManagerName { get; set; } = string.Empty;

    /// <summary>Where the money is, in words (e.g. „soukromý účet správce“) — never a full account number.</summary>
    public string? AccountDescription { get; set; }

    public bool Active { get; set; } = true;
}

/// <summary>One record of an off-book fund (T10): a call, a contribution, an expense or a settlement.</summary>
public class FundRecord
{
    public string Id { get; set; } = string.Empty;
    public string FundId { get; set; } = string.Empty;
    public FundRecordKind Kind { get; set; }

    /// <summary>Contribution / expense / settlement day; for a call its issue day.</summary>
    public DateOnly Date { get; set; }

    /// <summary>CZK, positive; for a call the amount per house.</summary>
    public decimal Amount { get; set; }

    /// <summary>Call text, expense description, note.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Call: its due date.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Call: the houses asked to contribute.</summary>
    public List<string> HouseIds { get; set; } = [];

    /// <summary>Contribution: the house and, optionally, the call it answers; the method (převod, hotovost).</summary>
    public string? HouseId { get; set; }

    public string? CallId { get; set; }
    public string? Method { get; set; }

    /// <summary>Expense: who paid it in advance (null = paid from the fund); whether there is a receipt.</summary>
    public string? PaidBy { get; set; }

    public bool HasReceipt { get; set; }

    /// <summary>Settlement: the expense it reimburses and to whom.</summary>
    public string? ExpenseId { get; set; }

    public string? PaidTo { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
