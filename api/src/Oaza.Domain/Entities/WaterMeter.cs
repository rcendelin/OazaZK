using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

public class WaterMeter
{
    public string Id { get; set; } = string.Empty;
    public string MeterNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public MeterType Type { get; set; }
    public string? HouseId { get; set; }
    public DateTime InstallationDate { get; set; }

    /// <summary>
    /// Physical radio address of the meter (e.g. wM-Bus "Address" 22040724) used to
    /// match readings imported from a meter-reader clipboard export.
    /// </summary>
    public string? RadioAddress { get; set; }
}
