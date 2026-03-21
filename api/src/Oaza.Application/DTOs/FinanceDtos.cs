namespace Oaza.Application.DTOs;

public class CreateFinanceRequest
{
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class UpdateFinanceRequest
{
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
}

public record FinanceResponse(
    string Id,
    int Year,
    string Type,
    string Category,
    decimal Amount,
    DateTime Date,
    string Description,
    bool HasAttachment);

public record FinanceSummaryResponse(
    int Year,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal Balance,
    List<CategorySummary> Categories);

public record CategorySummary(
    string Category,
    decimal Income,
    decimal Expenses);
