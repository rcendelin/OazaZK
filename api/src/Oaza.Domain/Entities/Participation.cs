namespace Oaza.Domain.Entities;

/// <summary>
/// Effective-dated participation of a house in a component (T02), e.g. only the 4 houses
/// connected to the waterworks pay its electricity from 1. 11. 2023 (R2). Intervals of one
/// house on one component must not overlap.
/// </summary>
public class Participation
{
    public string Id { get; set; } = string.Empty;
    public string ComponentId { get; set; } = string.Empty;
    public string HouseId { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }

    /// <summary>Last participating day (inclusive); null = open-ended.</summary>
    public DateOnly? ValidTo { get; set; }

    /// <summary>Percent for <c>PERCENT</c>, static weight for <c>RATIO</c>; ignored by <c>EQUAL</c> and <c>METERED</c>.</summary>
    public decimal? Weight { get; set; }
}
