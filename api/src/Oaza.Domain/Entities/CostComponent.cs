using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

/// <summary>
/// A kind of shared cost that is allocated to houses (T02): water PVK, water losses,
/// electricity for the waterworks, common lighting, septic tank emptying…
/// </summary>
public class CostComponent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Short stable code, unique (e.g. <c>VODA_PVK</c>); natural key for the seed import (T13).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>First accounted day (e.g. 1. 11. 2023 for water PVK, R1).</summary>
    public DateOnly StartDate { get; set; }

    public AllocationBasis AllocationBasis { get; set; }
    public bool Active { get; set; } = true;
    public string? Note { get; set; }
}
