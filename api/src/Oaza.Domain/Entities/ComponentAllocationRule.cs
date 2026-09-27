using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

/// <summary>
/// Effective-dated allocation method of a component (T02). Rules of one component
/// must not overlap; a change of method (e.g. water losses EQUAL → RATIO after a vote, O1)
/// is a new rule from its effective date.
/// </summary>
public class ComponentAllocationRule
{
    public string Id { get; set; } = string.Empty;
    public string ComponentId { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }

    /// <summary>Last valid day (inclusive); null = open-ended.</summary>
    public DateOnly? ValidTo { get; set; }

    public AllocationMethod Method { get; set; }

    /// <summary>
    /// For <see cref="AllocationMethod.Ratio"/>: where the weights come from (e.g. the code of the component whose
    /// consumption is the ratio). Null = static <see cref="Participation.Weight"/>. Resolved by the allocation (T05/T06).
    /// </summary>
    public string? RatioSource { get; set; }

    /// <summary>Why the rule was set or changed (e.g. „hlasování schůze 10/2026“); audited.</summary>
    public string? Reason { get; set; }
}
