namespace Oaza.Application.DTOs;

public record ChartDataPoint(int Year, int Month, string Label, decimal Consumption);

public record ChartResponse(
    string? HouseId,
    string? HouseName,
    List<ChartDataPoint> DataPoints);
