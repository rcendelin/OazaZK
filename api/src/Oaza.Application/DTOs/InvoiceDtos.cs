namespace Oaza.Application.DTOs;

public class InvoiceLineItemDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public decimal StartReading { get; set; }
    public decimal EndReading { get; set; }
    public decimal ConsumptionM3 { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal AmountExclVat { get; set; }
}

public class CreateInvoiceRequest
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal VatRatePercent { get; set; }
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();
}

public class UpdateInvoiceRequest
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal VatRatePercent { get; set; }
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();
}

public class InvoiceResponse
{
    public string Id { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }          // celková částka vč. DPH (Σ řádků × (1 + DPH))
    public decimal ConsumptionM3 { get; set; }   // celková spotřeba (Σ řádků)
    public decimal VatRatePercent { get; set; }
    public string? AttachmentBlobName { get; set; }
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();
}
