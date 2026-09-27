using Oaza.Domain.Enums;

namespace Oaza.Application.DTOs;

// Cost components, allocation rules and participation (T02). Calendar days are DateOnly ("yyyy-MM-dd" in JSON).

public class CostComponentResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public AllocationBasis AllocationBasis { get; set; }
    public bool Active { get; set; }
    public string? Note { get; set; }

    /// <summary>Method of the rule valid today (Europe/Prague); null when none.</summary>
    public AllocationMethod? CurrentMethod { get; set; }

    /// <summary>Number of houses participating today.</summary>
    public int CurrentParticipants { get; set; }
}

public class AllocationRuleResponse
{
    public string Id { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public AllocationMethod Method { get; set; }
    public string? RatioSource { get; set; }
    public string? Reason { get; set; }
}

public class ParticipationResponse
{
    public string Id { get; set; } = string.Empty;
    public string HouseId { get; set; } = string.Empty;
    public string HouseName { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal? Weight { get; set; }
}

public class CostComponentDetailResponse
{
    public CostComponentResponse Component { get; set; } = new();
    public List<AllocationRuleResponse> Rules { get; set; } = [];
    public List<ParticipationResponse> Participations { get; set; } = [];

    /// <summary>Last day fixed by an interim closing; changes must start after it. Null = nothing closed.</summary>
    public DateOnly? LastClosedDay { get; set; }
}

public class AllocationSegmentResponse
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public int Days { get; set; }
    public AllocationMethod? Method { get; set; }
    public List<ParticipationResponse> Participants { get; set; } = [];
}

public class CreateCostComponentRequest
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public AllocationBasis AllocationBasis { get; set; }

    /// <summary>Method of the first rule from <see cref="StartDate"/>; default Metered for metered components, else Equal.</summary>
    public AllocationMethod? Method { get; set; }

    public string? Note { get; set; }
}

public class UpdateCostComponentRequest
{
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public string? Note { get; set; }
}

public class AddAllocationRuleRequest
{
    public DateOnly ValidFrom { get; set; }
    public AllocationMethod Method { get; set; }
    public string? RatioSource { get; set; }

    /// <summary>Required, e.g. „hlasování schůze 10/2026“.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class AddParticipationRequest
{
    public string HouseId { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal? Weight { get; set; }
    public string? Reason { get; set; }
}

public class EndParticipationRequest
{
    /// <summary>Last participating day (inclusive).</summary>
    public DateOnly ValidTo { get; set; }
    public string? Reason { get; set; }
}
