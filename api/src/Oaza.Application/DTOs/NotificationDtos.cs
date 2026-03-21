namespace Oaza.Application.DTOs;

public class SendNotificationRequest
{
    public string Type { get; set; } = string.Empty;
    public string? PeriodId { get; set; }
    public int? Year { get; set; }
    public int? Month { get; set; }
}
