using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

public class WaterMeter
{
    public string Id { get; set; } = string.Empty;
    public string MeterNumber { get; set; } = string.Empty;
    public MeterType Type { get; set; }
    public string? HouseId { get; set; }
    public DateTime InstallationDate { get; set; }
}
