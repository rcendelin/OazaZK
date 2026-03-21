namespace Oaza.Application.DTOs;

public class CreateAdvanceRequest
{
    public string HouseId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
}

public class UpdateAdvanceRequest
{
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
}

public class AdvanceResponse
{
    public string HouseId { get; set; } = string.Empty;
    public string? HouseName { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
}
