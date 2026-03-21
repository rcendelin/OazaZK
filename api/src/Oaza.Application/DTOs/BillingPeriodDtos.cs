namespace Oaza.Application.DTOs;

public class CreateBillingPeriodRequest
{
    public string Name { get; set; } = string.Empty;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
}

public class BillingPeriodResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? TotalInvoiceAmount { get; set; }
}
