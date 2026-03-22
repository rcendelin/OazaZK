namespace Oaza.Application.DTOs;

public class CreateMeterRequest
{
    public string MeterNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "Main" or "Individual"
    public string? HouseId { get; set; }
}

public class UpdateMeterRequest
{
    public string MeterNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? HouseId { get; set; }
}

public class MeterResponse
{
    public string Id { get; set; } = string.Empty;
    public string MeterNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? HouseId { get; set; }
    public string? HouseName { get; set; }
    public DateTime InstallationDate { get; set; }
}
