namespace Oaza.Domain.Entities;

public class SupplierInvoice
{
    public string Id { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal ConsumptionM3 { get; set; }
    public string? AttachmentBlobName { get; set; }
}
