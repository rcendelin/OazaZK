namespace Oaza.Domain.Entities;

public class Settlement
{
    public string PeriodId { get; set; } = string.Empty;
    public string HouseId { get; set; } = string.Empty;
    public decimal ConsumptionM3 { get; set; }
    public decimal SharePercent { get; set; }
    public decimal CalculatedAmount { get; set; }
    public decimal TotalAdvances { get; set; }
    public decimal Balance { get; set; }
    public decimal LossAllocatedM3 { get; set; }
}
