using System.Globalization;
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
/// Supplier advances, settlements and one-off costs of components (T06, R6, O3). An entry is allocated to the
/// houses by the component's rules and participation over its period (<see cref="CostEntryAllocation"/>); how it
/// was paid never changes the allocation. Entries of a metered component (water PVK) are the supplier invoices —
/// they carry the invoiced m³ and serve the price and reconciliation (T05), not a direct allocation.
/// </summary>
public class CostEntriesUseCase
{
    public const string CostEntryEntity = "CostEntry";

    public const int MaxNoteLength = 2000;
    public const int MaxSupplierLength = 200;
    public const decimal MaxAmount = 100_000_000m;

    private readonly ICostEntryRepository _entries;
    private readonly ICostComponentRepository _components;
    private readonly IComponentAllocationRuleRepository _rules;
    private readonly IParticipationRepository _participations;
    private readonly IHouseRepository _houses;
    private readonly IClosingBoundary _closingBoundary;
    private readonly IAuditLogger _audit;

    public CostEntriesUseCase(
        ICostEntryRepository entries,
        ICostComponentRepository components,
        IComponentAllocationRuleRepository rules,
        IParticipationRepository participations,
        IHouseRepository houses,
        IClosingBoundary closingBoundary,
        IAuditLogger audit)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
        _closingBoundary = closingBoundary ?? throw new ArgumentNullException(nameof(closingBoundary));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>Entries of a component whose period overlaps <c>[from, to]</c> (both optional), oldest first.</summary>
    public async Task<IReadOnlyList<CostEntryResponse>> ListAsync(string componentId, DateOnly? from, DateOnly? to)
    {
        var component = await GetComponentAsync(componentId);
        var lastClosed = await _closingBoundary.GetLastClosedDayAsync();
        var entries = await _entries.GetByComponentAsync(componentId);
        return entries
            .Where(e => (from is null || e.PeriodTo >= from) && (to is null || e.PeriodFrom <= to))
            .OrderBy(e => e.PeriodFrom)
            .ThenBy(e => e.Type)
            .Select(e => ToResponse(e, component, lastClosed))
            .ToList();
    }

    public async Task<CostEntryResponse> CreateAsync(string componentId, SaveCostEntryRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var component = await GetComponentAsync(componentId);
        var entry = new CostEntry
        {
            Id = Guid.NewGuid().ToString(), ComponentId = componentId, CorrectionOf = Clean(request.CorrectionOf), ExternalRef = Clean(request.ExternalRef),
        };
        if (entry.ExternalRef is { } externalRef && (await _entries.GetAllAsync()).Any(e => e.ExternalRef == externalRef))
            throw new AppException($"Náklad s označením {externalRef} už existuje.", 409);
        Apply(entry, request);

        // An entry reaching into a closed period is a correction: split by the original segments, booked on the first open day (T08).
        var lastClosed = await _closingBoundary.GetLastClosedDayAsync();
        if (lastClosed is { } closed && entry.PeriodFrom <= closed)
        {
            if (Clean(request.Reason) is null)
                throw new BusinessRuleException([$"Období nákladu zasahuje do uzavřeného období (mezizávěrka k {Day(closed)}). Zaúčtuje se jako opravný záznam k {Day(closed.AddDays(1))} — uveďte důvod."]);
            entry.PostingDate = closed.AddDays(1);
        }
        await ValidateAsync(component, entry);

        await _entries.UpsertAsync(entry);
        await _audit.LogAsync(CostEntryEntity, entry.Id, AuditActions.Create, null, entry, actor, Clean(request.Reason));
        return ToResponse(entry, component, await _closingBoundary.GetLastClosedDayAsync());
    }

    /// <summary>Generates a series of advances of the same amount (the „Opakovaná záloha“ form).</summary>
    public async Task<IReadOnlyList<CostEntryResponse>> CreateRecurringAsync(string componentId, RecurringAdvanceRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var component = await GetComponentAsync(componentId);
        IReadOnlyList<DateRange> periods;
        try
        {
            periods = RecurringAdvance.Periods(request.From, request.To, request.Periodicity);
        }
        catch (ArgumentException ex)
        {
            throw new BusinessRuleException([ex.Message.Split(" (Parameter")[0]]);
        }

        var entries = periods.Select(p => new CostEntry
        {
            Id = Guid.NewGuid().ToString(),
            ComponentId = componentId,
            Type = CostEntryType.Advance,
            PeriodFrom = p.From,
            PeriodTo = p.To,
            Amount = request.Amount,
            Supplier = Clean(request.Supplier),
            PaidFrom = request.PaidFrom,
            Note = Clean(request.Note),
        }).ToList();

        // Validate the whole series before writing anything.
        foreach (var entry in entries)
            await ValidateAsync(component, entry);

        var lastClosed = await _closingBoundary.GetLastClosedDayAsync();
        var result = new List<CostEntryResponse>(entries.Count);
        foreach (var entry in entries)
        {
            await _entries.UpsertAsync(entry);
            await _audit.LogAsync(CostEntryEntity, entry.Id, AuditActions.Create, null, entry, actor, "Opakovaná záloha");
            result.Add(ToResponse(entry, component, lastClosed));
        }
        return result;
    }

    public async Task<CostEntryResponse> UpdateAsync(string componentId, string entryId, SaveCostEntryRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var component = await GetComponentAsync(componentId);
        var entry = await _entries.GetAsync(componentId, entryId) ?? throw new NotFoundException(CostEntryEntity, entryId);
        await EnsureOpenAsync(entry.LockDate);

        var before = Copy(entry);
        Apply(entry, request);
        if (entry.PostingDate is null && entry.PeriodFrom != before.PeriodFrom && await _closingBoundary.GetLastClosedDayAsync() is { } closed && entry.PeriodFrom <= closed)
            throw new BusinessRuleException([$"Nové období zasahuje do uzavřeného období (mezizávěrka k {Day(closed)}) — zadejte místo úpravy opravný záznam."]);
        await ValidateAsync(component, entry);

        await _entries.UpsertAsync(entry);
        await _audit.LogAsync(CostEntryEntity, entry.Id, AuditActions.Update, before, entry, actor, Clean(request.Reason));
        return ToResponse(entry, component, await _closingBoundary.GetLastClosedDayAsync());
    }

    public async Task DeleteAsync(string componentId, string entryId, string? reason, AuditActor actor)
    {
        await GetComponentAsync(componentId);
        var entry = await _entries.GetAsync(componentId, entryId) ?? throw new NotFoundException(CostEntryEntity, entryId);
        await EnsureOpenAsync(entry.LockDate);

        await _entries.DeleteAsync(componentId, entryId);
        await _audit.LogAsync(CostEntryEntity, entry.Id, AuditActions.Delete, entry, null, actor, Clean(reason));
    }

    /// <summary>
    /// Drill-down of one entry: its segments (constant rule and participation, cut at month starts so the cost
    /// arises every month, R6), the part of the amount in each and every house's share.
    /// </summary>
    public async Task<CostEntryAllocationResponse> GetAllocationAsync(string componentId, string entryId)
    {
        var component = await GetComponentAsync(componentId);
        if (component.AllocationBasis == AllocationBasis.Metered)
            throw new BusinessRuleException(["Tato složka se rozpočítává podle odečtů; faktury slouží k určení ceny za m³ a k odsouhlasení."]);
        var entry = await _entries.GetAsync(componentId, entryId) ?? throw new NotFoundException(CostEntryEntity, entryId);

        var shares = await AllocateAsync(entry);
        var names = (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name);
        CostShareResponse Share(string houseId, decimal weight, decimal amount) =>
            new() { HouseId = houseId, HouseName = names.GetValueOrDefault(houseId, houseId), Weight = weight, Amount = amount };

        return new CostEntryAllocationResponse
        {
            Entry = ToResponse(entry, component, await _closingBoundary.GetLastClosedDayAsync()),
            Segments = shares
                .GroupBy(s => s.Segment)
                .OrderBy(g => g.Key.From)
                .Select(g => new CostSegmentResponse
                {
                    From = g.Key.From,
                    To = g.Key.To,
                    Days = g.Key.Days,
                    Method = g.First().Method,
                    Amount = g.First().SegmentAmount,
                    Shares = g.Select(s => Share(s.HouseId, s.Weight, s.Amount)).ToList(),
                })
                .ToList(),
            HouseTotals = shares
                .GroupBy(s => s.HouseId)
                .Select(g => Share(g.Key, 0m, g.Sum(s => s.Amount)))
                .OrderBy(s => s.HouseName, StringComparer.CurrentCulture)
                .ToList(),
        };
    }

    // ───────────────────── Helpers ─────────────────────

    private async Task<IReadOnlyList<CostShare>> AllocateAsync(CostEntry entry)
    {
        var rules = await _rules.GetByComponentAsync(entry.ComponentId);
        var participations = await _participations.GetByComponentAsync(entry.ComponentId);
        var period = new DateRange(entry.PeriodFrom, entry.PeriodTo);
        try
        {
            return CostEntryAllocation.Allocate(entry.Amount, period, rules, participations, CostEntryAllocation.MonthStarts(period));
        }
        catch (InvalidOperationException ex)
        {
            throw new BusinessRuleException([ex.Message]);
        }
    }

    private async Task ValidateAsync(CostComponent component, CostEntry entry)
    {
        var errors = new List<string>();
        if (entry.PeriodFrom == default || entry.PeriodTo == default)
            errors.Add("Období od a do je povinné.");
        else if (entry.PeriodFrom > entry.PeriodTo)
            errors.Add("Období musí začínat nejpozději v den konce.");
        else if (entry.PeriodFrom < component.StartDate)
            errors.Add($"Období začíná před začátkem účtování složky ({Day(component.StartDate)}); starší náklady se nezadávají (R3).");

        if (entry.Note?.Length > MaxNoteLength)
            errors.Add($"Poznámka může mít nejvýš {MaxNoteLength} znaků.");
        if (entry.Supplier?.Length > MaxSupplierLength)
            errors.Add($"Dodavatel může mít nejvýš {MaxSupplierLength} znaků.");
        if (Math.Abs(entry.Amount) > MaxAmount)
            errors.Add("Částka je mimo rozumný rozsah.");
        if (decimal.Round(entry.Amount, 2) != entry.Amount)
            errors.Add("Částka může mít nejvýš dvě desetinná místa.");
        if (entry.Type == CostEntryType.Advance && entry.Amount <= 0)
            errors.Add("Záloha musí být kladná.");
        else if (entry.Amount == 0)
            errors.Add("Částka nesmí být nulová.");

        if (component.AllocationBasis == AllocationBasis.Metered)
        {
            if (entry.QuantityM3 is null or < 0)
                errors.Add("U faktury za vodu zadejte fakturované množství v m³.");
        }
        else if (entry.QuantityM3 is not null)
        {
            errors.Add("Množství v m³ se zadává jen u složky účtované podle odečtů.");
        }
        ThrowIfAny(errors);

        await EnsureOpenAsync(entry.LockDate);
        if (component.AllocationBasis == AllocationBasis.CostEntries)
            await AllocateAsync(entry); // an entry that cannot be allocated (nobody participates) is rejected
    }

    private async Task EnsureOpenAsync(DateOnly periodFrom) =>
        ThrowIfAny(ComponentValidation.CheckNotClosed(periodFrom, await _closingBoundary.GetLastClosedDayAsync()));

    private async Task<CostComponent> GetComponentAsync(string componentId) =>
        await _components.GetAsync(PartitionKeys.CostComponent, componentId) ?? throw new NotFoundException("CostComponent", componentId);

    private static void Apply(CostEntry entry, SaveCostEntryRequest request)
    {
        entry.Type = request.Type;
        entry.PeriodFrom = request.PeriodFrom;
        entry.PeriodTo = request.PeriodTo;
        entry.Amount = request.Amount;
        entry.QuantityM3 = request.QuantityM3;
        entry.Supplier = Clean(request.Supplier);
        entry.DocumentId = Clean(request.DocumentId);
        entry.PaidFrom = request.PaidFrom;
        entry.Note = Clean(request.Note);
    }

    private static CostEntry Copy(CostEntry e) => new()
    {
        Id = e.Id, ComponentId = e.ComponentId, Type = e.Type, PeriodFrom = e.PeriodFrom, PeriodTo = e.PeriodTo, Amount = e.Amount,
        QuantityM3 = e.QuantityM3, Supplier = e.Supplier, DocumentId = e.DocumentId, PaidFrom = e.PaidFrom, Note = e.Note,
        PostingDate = e.PostingDate, CorrectionOf = e.CorrectionOf, ExternalRef = e.ExternalRef,
    };

    private static CostEntryResponse ToResponse(CostEntry e, CostComponent component, DateOnly? lastClosed) => new()
    {
        Id = e.Id,
        ComponentId = e.ComponentId,
        ComponentName = component.Name,
        Type = e.Type,
        PeriodFrom = e.PeriodFrom,
        PeriodTo = e.PeriodTo,
        Days = e.PeriodTo.DayNumber - e.PeriodFrom.DayNumber + 1,
        Amount = e.Amount,
        QuantityM3 = e.QuantityM3,
        Supplier = e.Supplier,
        DocumentId = e.DocumentId,
        PaidFrom = e.PaidFrom,
        Note = e.Note,
        PostingDate = e.PostingDate,
        CorrectionOf = e.CorrectionOf,
        Locked = lastClosed is { } closed && e.LockDate <= closed,
    };

    private static void ThrowIfAny(IReadOnlyList<string> errors)
    {
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);
}
