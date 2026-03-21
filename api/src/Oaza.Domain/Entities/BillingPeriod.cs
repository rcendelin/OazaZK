using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

public class BillingPeriod
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public BillingPeriodStatus Status { get; set; }
}
