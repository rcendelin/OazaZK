namespace Oaza.Domain.Enums;

/// <summary>How a component's cost is split among the participating houses (T02).</summary>
public enum AllocationMethod
{
    /// <summary>By each house's metered consumption.</summary>
    Metered,

    /// <summary>Equal parts among the participants active in the segment.</summary>
    Equal,

    /// <summary>In proportion to weights — static <c>Participation.Weight</c>, or the rule's <c>RatioSource</c> (e.g. consumption).</summary>
    Ratio,

    /// <summary>Fixed percentages in <c>Participation.Weight</c>; must sum to 100 % on every day.</summary>
    Percent,
}
