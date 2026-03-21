namespace Oaza.Domain.Entities;

public class AdvancePayment
{
    public string HouseId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
}
