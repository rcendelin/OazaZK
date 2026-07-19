namespace Oaza.Application.DTOs;

/// <summary>
/// One row in the unified "received invoices" overview — either a water
/// SupplierInvoice or an expense FinancialRecord. Read-only projection.
/// </summary>
public record ReceivedInvoiceResponse(
    string Source,        // "voda" | "ostatni"
    string Id,
    DateTime Date,        // IssuedDate (voda) | Date (ostatni)
    string Category,      // "voda" | FinancialRecord.Category
    string Description,   // InvoiceNumber (voda) | Description (ostatni)
    decimal Amount,
    DateTime? DueDate,
    bool CountsTowardWaterSettlement,
    bool HasAttachment,
    string? AttachmentDownloadPath);
