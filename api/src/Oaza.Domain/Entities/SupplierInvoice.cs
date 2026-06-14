namespace Oaza.Domain.Entities;

public class SupplierInvoice
{
    public string Id { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }          // celková částka VČETNĚ DPH (dopočítaná z řádků)
    public decimal ConsumptionM3 { get; set; }   // celková spotřeba (součet řádků)
    public decimal VatRatePercent { get; set; }  // sazba DPH v % (např. 12)
    public string? AttachmentBlobName { get; set; }

    /// <summary>Dílčí odečty na faktuře. Faktura je jedna na celkovou částku, ale obsahuje více řádků.</summary>
    public List<InvoiceLineItem> LineItems { get; set; } = new();
}
