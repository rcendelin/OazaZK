using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

public class FinancialRecord
{
    public string Id { get; set; } = string.Empty;
    public int Year { get; set; }
    public FinancialRecordType Type { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? AttachmentBlobName { get; set; }
}
