using System.Text.RegularExpressions;
using Oaza.Application.Audit;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Application.UseCases;

/// <summary>
/// Configuration of cost components (T02): the components, their effective-dated
/// allocation rules and house participation. Every write is validated against the
/// business rules (<see cref="ComponentValidation"/>), must not reach into a closed
/// period and is recorded in the audit log.
/// </summary>
public partial class CostComponentsUseCase
{
    public const string ComponentEntity = "CostComponent";
    public const string RuleEntity = "ComponentAllocationRule";
    public const string ParticipationEntity = "Participation";

    /// <summary>Longest range of a segments query (10 years).</summary>
    private const int MaxSegmentRangeDays = 3660;

    private readonly ICostComponentRepository _components;
    private readonly IComponentAllocationRuleRepository _rules;
    private readonly IParticipationRepository _participations;
    private readonly IHouseRepository _houses;
    private readonly IClosingBoundary _closingBoundary;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public CostComponentsUseCase(
        ICostComponentRepository components,
        IComponentAllocationRuleRepository rules,
        IParticipationRepository participations,
        IHouseRepository houses,
        IClosingBoundary closingBoundary,
        IAuditLogger audit,
        IClock clock)
    {
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
        _closingBoundary = closingBoundary ?? throw new ArgumentNullException(nameof(closingBoundary));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    // ───────────────────── Reads ─────────────────────

    public async Task<IReadOnlyList<CostComponentResponse>> ListAsync()
    {
        var components = await _components.GetAllComponentsAsync();
        var result = new List<CostComponentResponse>(components.Count);
        foreach (var component in components.OrderBy(c => c.Name, StringComparer.CurrentCulture))
        {
            var rules = await _rules.GetByComponentAsync(component.Id);
            var participations = await _participations.GetByComponentAsync(component.Id);
            result.Add(ToResponse(component, rules, participations));
        }
        return result;
    }

    public async Task<CostComponentDetailResponse> GetDetailAsync(string componentId)
    {
        var component = await GetComponentAsync(componentId);
        var rules = await _rules.GetByComponentAsync(componentId);
        var participations = await _participations.GetByComponentAsync(componentId);
        var houseNames = await GetHouseNamesAsync();

        return new CostComponentDetailResponse
        {
            Component = ToResponse(component, rules, participations),
            Rules = rules.OrderBy(r => r.ValidFrom).Select(ToResponse).ToList(),
            Participations = participations
                .OrderBy(p => houseNames.GetValueOrDefault(p.HouseId, p.HouseId), StringComparer.CurrentCulture)
                .ThenBy(p => p.ValidFrom)
                .Select(p => ToResponse(p, houseNames))
                .ToList(),
            LastClosedDay = await _closingBoundary.GetLastClosedDayAsync(),
        };
    }

    /// <summary>Segments of <c>[from, to]</c> in which the component's allocation is constant.</summary>
    public async Task<IReadOnlyList<AllocationSegmentResponse>> GetSegmentsAsync(string componentId, DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new AppException("Datum od musí být nejpozději v den data do.");
        var range = new DateRange(from, to);
        if (range.Days > MaxSegmentRangeDays)
            throw new AppException("Období může mít nejvýš 10 let.");

        await GetComponentAsync(componentId);
        var rules = await _rules.GetByComponentAsync(componentId);
        var participations = await _participations.GetByComponentAsync(componentId);
        var houseNames = await GetHouseNamesAsync();

        return AllocationSegments.Build(range, rules, participations)
            .Select(s => new AllocationSegmentResponse
            {
                From = s.Range.From,
                To = s.Range.To,
                Days = s.Range.Days,
                Method = s.Rule?.Method,
                Participants = s.Participants.Select(p => ToResponse(p, houseNames)).ToList(),
            })
            .ToList();
    }

    // ───────────────────── Components ─────────────────────

    public async Task<CostComponentResponse> CreateAsync(CreateCostComponentRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.Name?.Trim() ?? string.Empty;
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var errors = new List<string>();
        if (name.Length == 0)
            errors.Add("Název složky je povinný.");
        if (!CodePattern().IsMatch(code))
            errors.Add("Kód složky smí obsahovat jen písmena A–Z, číslice a podtržítko (např. VODA_PVK).");
        if (request.StartDate == default)
            errors.Add("Datum začátku účtování je povinné.");
        if (request.WaterRole != WaterRole.None && request.AllocationBasis != AllocationBasis.Metered)
            errors.Add("Roli ve vyúčtování vody může mít jen složka účtovaná podle odečtů.");
        ThrowIfAny(errors);

        var existing = await _components.GetAllComponentsAsync();
        if (request.WaterRole != WaterRole.None && existing.Any(c => c.WaterRole == request.WaterRole))
            throw new AppException(request.WaterRole == WaterRole.Consumption
                ? "Složka pro vodu PVK už existuje."
                : "Složka pro ztráty vody už existuje.", 409);
        if (existing.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            throw new AppException($"Složka s kódem {code} už existuje.", 409);

        var component = new CostComponent
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Code = code,
            StartDate = request.StartDate,
            AllocationBasis = request.AllocationBasis,
            WaterRole = request.WaterRole,
            Active = true,
            Note = Clean(request.Note),
        };
        var rule = new ComponentAllocationRule
        {
            Id = Guid.NewGuid().ToString(),
            ComponentId = component.Id,
            ValidFrom = component.StartDate,
            Method = request.Method ?? (component.AllocationBasis == AllocationBasis.Metered ? AllocationMethod.Metered : AllocationMethod.Equal),
            RatioSource = Clean(request.RatioSource),
            Reason = "Založení složky",
        };

        await _components.UpsertAsync(component);
        await _audit.LogAsync(ComponentEntity, component.Id, AuditActions.Create, null, component, actor);
        await _rules.UpsertAsync(rule);
        await _audit.LogAsync(RuleEntity, rule.Id, AuditActions.Create, null, rule, actor, rule.Reason);

        return ToResponse(component, [rule], []);
    }

    public async Task<CostComponentResponse> UpdateAsync(string componentId, UpdateCostComponentRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var component = await GetComponentAsync(componentId);
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new AppException("Název složky je povinný.");

        var before = Copy(component);
        component.Name = name;
        component.Active = request.Active;
        component.Note = Clean(request.Note);

        await _components.UpsertAsync(component);
        await _audit.LogAsync(ComponentEntity, component.Id, AuditActions.Update, before, component, actor);

        var rules = await _rules.GetByComponentAsync(componentId);
        var participations = await _participations.GetByComponentAsync(componentId);
        return ToResponse(component, rules, participations);
    }

    // ───────────────────── Rules ─────────────────────

    /// <summary>
    /// Switches the allocation method from <see cref="AddAllocationRuleRequest.ValidFrom"/>. The open-ended rule
    /// valid before that day is ended the day before; nothing before the effective date changes.
    /// </summary>
    public async Task<AllocationRuleResponse> AddRuleAsync(string componentId, AddAllocationRuleRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var component = await GetComponentAsync(componentId);
        var reason = Clean(request.Reason) ?? throw new BusinessRuleException(["Uveďte důvod změny metody (např. „hlasování schůze 10/2026“)."]);
        await EnsureValidFromAsync(component, request.ValidFrom);

        var rules = (await _rules.GetByComponentAsync(componentId)).ToList();
        var participations = await _participations.GetByComponentAsync(componentId);

        var ended = rules.SingleOrDefault(r => r.ValidTo is null && r.ValidFrom < request.ValidFrom);
        ComponentAllocationRule? endedBefore = null;
        if (ended is not null)
        {
            endedBefore = Copy(ended);
            ended.ValidTo = request.ValidFrom.AddDays(-1);
        }

        var rule = new ComponentAllocationRule
        {
            Id = Guid.NewGuid().ToString(),
            ComponentId = componentId,
            ValidFrom = request.ValidFrom,
            Method = request.Method,
            RatioSource = Clean(request.RatioSource),
            Reason = reason,
        };
        rules.Add(rule);
        ThrowIfAny([.. ComponentValidation.CheckRules(rules), .. ComponentValidation.CheckPercentSums(rules, participations)]);

        if (ended is not null)
        {
            await _rules.UpsertAsync(ended);
            await _audit.LogAsync(RuleEntity, ended.Id, AuditActions.Update, endedBefore, ended, actor, reason);
        }
        await _rules.UpsertAsync(rule);
        await _audit.LogAsync(RuleEntity, rule.Id, AuditActions.Create, null, rule, actor, reason);
        return ToResponse(rule);
    }

    /// <summary>Removes a rule entered by mistake; the rule ending the day before it is reopened to its end.</summary>
    public async Task DeleteRuleAsync(string componentId, string ruleId, string? reason, AuditActor actor)
    {
        await GetComponentAsync(componentId);
        var rules = (await _rules.GetByComponentAsync(componentId)).ToList();
        var rule = rules.SingleOrDefault(r => r.Id == ruleId) ?? throw new NotFoundException(RuleEntity, ruleId);
        if (rules.Count == 1)
            throw new BusinessRuleException(["Složka musí mít aspoň jedno pravidlo rozpočtu."]);
        ThrowIfAny(ComponentValidation.CheckNotClosed(rule.ValidFrom, await _closingBoundary.GetLastClosedDayAsync()));

        rules.Remove(rule);
        var previous = rules.SingleOrDefault(r => r.ValidTo == rule.ValidFrom.AddDays(-1));
        ComponentAllocationRule? previousBefore = null;
        if (previous is not null)
        {
            previousBefore = Copy(previous);
            previous.ValidTo = rule.ValidTo;
        }
        var participations = await _participations.GetByComponentAsync(componentId);
        ThrowIfAny([.. ComponentValidation.CheckRules(rules), .. ComponentValidation.CheckPercentSums(rules, participations)]);

        await _rules.DeleteAsync(componentId, ruleId);
        await _audit.LogAsync(RuleEntity, rule.Id, AuditActions.Delete, rule, null, actor, Clean(reason));
        if (previous is not null)
        {
            await _rules.UpsertAsync(previous);
            await _audit.LogAsync(RuleEntity, previous.Id, AuditActions.Update, previousBefore, previous, actor, Clean(reason));
        }
    }

    // ───────────────────── Participation ─────────────────────

    public async Task<ParticipationResponse> AddParticipationAsync(string componentId, AddParticipationRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await AddParticipationsAsync(componentId, [request], actor)).Single();
    }

    /// <summary>
    /// Adds several participations at once and validates them together — PERCENT weights must give 100 % only
    /// after the whole set is in (the seed import, T13).
    /// </summary>
    public async Task<IReadOnlyList<ParticipationResponse>> AddParticipationsAsync(string componentId, IReadOnlyList<AddParticipationRequest> requests, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var component = await GetComponentAsync(componentId);
        var houseNames = await GetHouseNamesAsync();
        var added = new List<Participation>();
        foreach (var request in requests)
        {
            if (!houseNames.ContainsKey(request.HouseId ?? string.Empty))
                throw new AppException("Dům neexistuje.", 404);
            await EnsureValidFromAsync(component, request.ValidFrom);
            added.Add(new Participation
            {
                Id = Guid.NewGuid().ToString(),
                ComponentId = componentId,
                HouseId = request.HouseId!,
                ValidFrom = request.ValidFrom,
                ValidTo = request.ValidTo,
                Weight = request.Weight,
            });
        }
        var participations = (await _participations.GetByComponentAsync(componentId)).Concat(added).ToList();
        var rules = await _rules.GetByComponentAsync(componentId);
        ThrowIfAny([.. ComponentValidation.CheckParticipations(participations), .. ComponentValidation.CheckPercentSums(rules, participations)]);

        for (var i = 0; i < added.Count; i++)
        {
            await _participations.UpsertAsync(added[i]);
            await _audit.LogAsync(ParticipationEntity, added[i].Id, AuditActions.Create, null, added[i], actor, Clean(requests[i].Reason));
        }
        return added.Select(p => ToResponse(p, houseNames)).ToList();
    }

    /// <summary>Ends (or moves the end of) a participation; the change applies from the first day that differs.</summary>
    public async Task<ParticipationResponse> EndParticipationAsync(string componentId, string participationId, EndParticipationRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        await GetComponentAsync(componentId);
        var participations = (await _participations.GetByComponentAsync(componentId)).ToList();
        var participation = participations.SingleOrDefault(p => p.Id == participationId) ?? throw new NotFoundException(ParticipationEntity, participationId);

        var changeFrom = participation.ValidTo is { } oldEnd && oldEnd < request.ValidTo ? oldEnd.AddDays(1) : request.ValidTo.AddDays(1);
        ThrowIfAny(ComponentValidation.CheckNotClosed(changeFrom, await _closingBoundary.GetLastClosedDayAsync()));

        var before = Copy(participation);
        participation.ValidTo = request.ValidTo;
        var rules = await _rules.GetByComponentAsync(componentId);
        ThrowIfAny([.. ComponentValidation.CheckParticipations(participations), .. ComponentValidation.CheckPercentSums(rules, participations)]);

        await _participations.UpsertAsync(participation);
        await _audit.LogAsync(ParticipationEntity, participation.Id, AuditActions.Update, before, participation, actor, Clean(request.Reason));
        return ToResponse(participation, await GetHouseNamesAsync());
    }

    /// <summary>Removes a participation entered by mistake (only when it lies wholly in the open period).</summary>
    public async Task DeleteParticipationAsync(string componentId, string participationId, string? reason, AuditActor actor)
    {
        await GetComponentAsync(componentId);
        var participations = (await _participations.GetByComponentAsync(componentId)).ToList();
        var participation = participations.SingleOrDefault(p => p.Id == participationId) ?? throw new NotFoundException(ParticipationEntity, participationId);
        ThrowIfAny(ComponentValidation.CheckNotClosed(participation.ValidFrom, await _closingBoundary.GetLastClosedDayAsync()));

        participations.Remove(participation);
        var rules = await _rules.GetByComponentAsync(componentId);
        ThrowIfAny(ComponentValidation.CheckPercentSums(rules, participations));

        await _participations.DeleteAsync(componentId, participationId);
        await _audit.LogAsync(ParticipationEntity, participation.Id, AuditActions.Delete, participation, null, actor, Clean(reason));
    }

    // ───────────────────── Helpers ─────────────────────

    private async Task<CostComponent> GetComponentAsync(string componentId) =>
        await _components.GetAsync(PartitionKeys.CostComponent, componentId) ?? throw new NotFoundException(ComponentEntity, componentId);

    private async Task EnsureValidFromAsync(CostComponent component, DateOnly validFrom)
    {
        var errors = new List<string>();
        if (validFrom < component.StartDate)
            errors.Add($"Změna nemůže začít před začátkem účtování složky ({component.StartDate:d. M. yyyy}).");
        errors.AddRange(ComponentValidation.CheckNotClosed(validFrom, await _closingBoundary.GetLastClosedDayAsync()));
        ThrowIfAny(errors);
    }

    private async Task<Dictionary<string, string>> GetHouseNamesAsync() =>
        (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name);

    private static void ThrowIfAny(IReadOnlyList<string> errors)
    {
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private CostComponentResponse ToResponse(CostComponent c, IReadOnlyList<ComponentAllocationRule> rules, IReadOnlyList<Participation> participations)
    {
        var today = _clock.Today;
        return new CostComponentResponse
        {
            Id = c.Id,
            Name = c.Name,
            Code = c.Code,
            StartDate = c.StartDate,
            AllocationBasis = c.AllocationBasis,
            WaterRole = c.WaterRole,
            Active = c.Active,
            Note = c.Note,
            CurrentMethod = rules.FirstOrDefault(r => AllocationSegments.IsActive(r.ValidFrom, r.ValidTo, today))?.Method,
            CurrentParticipants = participations.Count(p => AllocationSegments.IsActive(p.ValidFrom, p.ValidTo, today)),
        };
    }

    private static AllocationRuleResponse ToResponse(ComponentAllocationRule r) => new()
    {
        Id = r.Id,
        ValidFrom = r.ValidFrom,
        ValidTo = r.ValidTo,
        Method = r.Method,
        RatioSource = r.RatioSource,
        Reason = r.Reason,
    };

    private static ParticipationResponse ToResponse(Participation p, IReadOnlyDictionary<string, string> houseNames) => new()
    {
        Id = p.Id,
        HouseId = p.HouseId,
        HouseName = houseNames.GetValueOrDefault(p.HouseId, p.HouseId),
        ValidFrom = p.ValidFrom,
        ValidTo = p.ValidTo,
        Weight = p.Weight,
    };

    private static CostComponent Copy(CostComponent c) => new()
    {
        Id = c.Id, Name = c.Name, Code = c.Code, StartDate = c.StartDate, AllocationBasis = c.AllocationBasis, WaterRole = c.WaterRole, Active = c.Active, Note = c.Note,
    };

    private static ComponentAllocationRule Copy(ComponentAllocationRule r) => new()
    {
        Id = r.Id, ComponentId = r.ComponentId, ValidFrom = r.ValidFrom, ValidTo = r.ValidTo, Method = r.Method, RatioSource = r.RatioSource, Reason = r.Reason,
    };

    private static Participation Copy(Participation p) => new()
    {
        Id = p.Id, ComponentId = p.ComponentId, HouseId = p.HouseId, ValidFrom = p.ValidFrom, ValidTo = p.ValidTo, Weight = p.Weight,
    };

    [GeneratedRegex("^[A-Z0-9_]{2,40}$")]
    private static partial Regex CodePattern();
}
