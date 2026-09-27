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
/// Starting values without history (T03, R3–R5): ownership periods, meter states, fund shares and
/// component credits at the accounting start. A value is unique per (type, target, ownership period);
/// once an interim closing covers its date it can only be changed as an audited correction with a reason.
/// A meter opening value is also a real meter reading on that day, so every consumption calculation sees it.
/// </summary>
public class OpeningBalancesUseCase
{
    public const string OpeningBalanceEntity = "OpeningBalance";
    public const string OwnershipPeriodEntity = "OwnershipPeriod";
    public const string MeterReadingEntity = "MeterReading";

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private readonly IOpeningBalanceRepository _balances;
    private readonly IOwnershipPeriodRepository _periods;
    private readonly IHouseRepository _houses;
    private readonly IWaterMeterRepository _meters;
    private readonly IMeterReadingRepository _readings;
    private readonly ICostComponentRepository _components;
    private readonly IComponentAllocationRuleRepository _rules;
    private readonly IParticipationRepository _participations;
    private readonly IClosingBoundary _closingBoundary;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public OpeningBalancesUseCase(
        IOpeningBalanceRepository balances,
        IOwnershipPeriodRepository periods,
        IHouseRepository houses,
        IWaterMeterRepository meters,
        IMeterReadingRepository readings,
        ICostComponentRepository components,
        IComponentAllocationRuleRepository rules,
        IParticipationRepository participations,
        IClosingBoundary closingBoundary,
        IAuditLogger audit,
        IClock clock)
    {
        _balances = balances ?? throw new ArgumentNullException(nameof(balances));
        _periods = periods ?? throw new ArgumentNullException(nameof(periods));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
        _meters = meters ?? throw new ArgumentNullException(nameof(meters));
        _readings = readings ?? throw new ArgumentNullException(nameof(readings));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _closingBoundary = closingBoundary ?? throw new ArgumentNullException(nameof(closingBoundary));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    // ───────────────────── Ownership periods ─────────────────────

    public async Task<IReadOnlyList<OwnershipPeriodResponse>> GetOwnershipPeriodsAsync()
    {
        var houses = await _houses.GetByPartitionKeyAsync(PartitionKeys.House);
        var result = new List<OwnershipPeriodResponse>();
        foreach (var house in houses.OrderBy(h => h.Name, StringComparer.CurrentCulture))
        {
            var periods = await _periods.GetByHouseAsync(house.Id);
            result.AddRange(periods.OrderBy(p => p.ValidFrom).Select(p => ToResponse(p, house.Name)));
        }
        return result;
    }

    /// <summary>
    /// Gives every active house without an ownership period one from <paramref name="request"/>'s start day
    /// (owner = the house's contact person). Idempotent: houses that already have a period are left alone.
    /// </summary>
    public async Task<StartOwnershipResponse> StartOwnershipAsync(StartOwnershipRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartDate == default)
            throw new AppException("Zadejte datum začátku účtování.");

        var created = 0;
        var periods = new List<OwnershipPeriodResponse>();
        var houses = await _houses.GetByPartitionKeyAsync(PartitionKeys.House);
        foreach (var house in houses.Where(h => h.IsActive).OrderBy(h => h.Name, StringComparer.CurrentCulture))
        {
            var existing = await _periods.GetByHouseAsync(house.Id);
            if (existing.Count > 0)
            {
                periods.AddRange(existing.OrderBy(p => p.ValidFrom).Select(p => ToResponse(p, house.Name)));
                continue;
            }

            var period = new OwnershipPeriod
            {
                Id = OwnershipPeriod.KeyFor(house.Id, request.StartDate),
                HouseId = house.Id,
                OwnerName = string.IsNullOrWhiteSpace(house.ContactPerson) ? house.Name : house.ContactPerson,
                Contact = string.IsNullOrWhiteSpace(house.Email) ? null : house.Email,
                ValidFrom = request.StartDate,
            };
            await _periods.UpsertAsync(period);
            await _audit.LogAsync(OwnershipPeriodEntity, period.Id, AuditActions.Create, null, period, actor, "Start účtování");
            periods.Add(ToResponse(period, house.Name));
            created++;
        }
        return new StartOwnershipResponse { Created = created, Periods = periods };
    }

    // ───────────────────── Opening balances ─────────────────────

    public async Task<IReadOnlyList<OpeningBalanceResponse>> ListAsync()
    {
        var balances = await _balances.GetAllBalancesAsync();
        var names = await LoadNamesAsync();
        var lastClosed = await _closingBoundary.GetLastClosedDayAsync();
        return balances
            .OrderBy(b => b.Type)
            .ThenBy(b => b.Date)
            .ThenBy(b => b.Key, StringComparer.Ordinal)
            .Select(b => ToResponse(b, names, lastClosed))
            .ToList();
    }

    /// <summary>Creates an opening value; 409 when one already exists for the same type, target and ownership period.</summary>
    public async Task<OpeningBalanceResponse> CreateAsync(SaveOpeningBalanceRequest request, AuditActor actor)
    {
        var balance = await BuildAsync(request);
        if (await _balances.GetAsync(PartitionKeys.OpeningBalance, balance.Key) is not null)
            throw new AppException("Počáteční stav pro tento dům, vodoměr nebo složku a období vlastnictví už existuje — upravte ho.", 409);

        var lastClosed = await _closingBoundary.GetLastClosedDayAsync();
        if (IsLocked(balance.Date, lastClosed))
            throw new BusinessRuleException([$"Datum {Day(balance.Date)} je uzavřené mezizávěrkou k {Day(lastClosed!.Value)}; nový počáteční stav do něj nelze přidat."]);

        await SyncMeterReadingAsync(balance, actor, null);
        await _balances.UpsertAsync(balance);
        await _audit.LogAsync(OpeningBalanceEntity, balance.Key, AuditActions.Create, null, balance, actor, Clean(request.Reason));
        return ToResponse(balance, await LoadNamesAsync(), lastClosed);
    }

    /// <summary>
    /// Changes an existing opening value (same type, target and ownership period). A value whose date is fixed
    /// by an interim closing can only be corrected with a reason; the change is logged as a correction.
    /// </summary>
    public async Task<OpeningBalanceResponse> UpdateAsync(string key, SaveOpeningBalanceRequest request, AuditActor actor)
    {
        var existing = await _balances.GetAsync(PartitionKeys.OpeningBalance, key) ?? throw new NotFoundException(OpeningBalanceEntity, key);
        var balance = await BuildAsync(request);
        if (balance.Key != existing.Key)
            throw new AppException("Typ, dům, vodoměr, složku ani období vlastnictví nelze změnit — smažte stav a založte nový.");

        var lastClosed = await _closingBoundary.GetLastClosedDayAsync();
        var locked = IsLocked(existing.Date, lastClosed) || IsLocked(balance.Date, lastClosed);
        var reason = Clean(request.Reason);
        if (locked && reason is null)
            throw new BusinessRuleException(["Počáteční stav je už uzavřený mezizávěrkou — opravu lze uložit jen s uvedeným důvodem."]);

        await SyncMeterReadingAsync(balance, actor, existing);
        await _balances.UpsertAsync(balance);
        await _audit.LogAsync(OpeningBalanceEntity, balance.Key, locked ? AuditActions.Correction : AuditActions.Update, existing, balance, actor, reason);
        return ToResponse(balance, await LoadNamesAsync(), lastClosed);
    }

    /// <summary>Deletes an opening value entered by mistake; not possible once an interim closing covers it. The meter reading stays.</summary>
    public async Task DeleteAsync(string key, string? reason, AuditActor actor)
    {
        var existing = await _balances.GetAsync(PartitionKeys.OpeningBalance, key) ?? throw new NotFoundException(OpeningBalanceEntity, key);
        var lastClosed = await _closingBoundary.GetLastClosedDayAsync();
        if (IsLocked(existing.Date, lastClosed))
            throw new BusinessRuleException(["Počáteční stav je uzavřený mezizávěrkou a nelze ho smazat — použijte opravu s důvodem."]);

        await _balances.DeleteAsync(PartitionKeys.OpeningBalance, key);
        await _audit.LogAsync(OpeningBalanceEntity, key, AuditActions.Delete, existing, null, actor, Clean(reason));
    }

    /// <summary>
    /// How a component credit would split among the houses (O2): the component's rule and participants on
    /// <paramref name="date"/>. E.g. −20 000 Kč, EQUAL among 4 houses → 4 × −5 000 Kč (S2).
    /// </summary>
    public async Task<ComponentCreditPreviewResponse> PreviewComponentCreditAsync(string componentId, DateOnly date, decimal value)
    {
        var component = await _components.GetAsync(PartitionKeys.CostComponent, componentId)
            ?? throw new NotFoundException("CostComponent", componentId);
        if (decimal.Round(value, 2) != value)
            throw new AppException("Částka může mít nejvýš dvě desetinná místa.");

        var rules = await _rules.GetByComponentAsync(component.Id);
        var participations = await _participations.GetByComponentAsync(component.Id);
        var segment = AllocationSegments.Build(new DateRange(date, date), rules, participations).Single();

        IReadOnlyList<HouseShare> shares;
        try
        {
            shares = SegmentAllocation.Split(value, segment);
        }
        catch (InvalidOperationException ex)
        {
            throw new BusinessRuleException([ex.Message]);
        }

        var names = (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name);
        return new ComponentCreditPreviewResponse
        {
            ComponentId = component.Id,
            Date = date,
            Method = segment.Rule!.Method,
            Total = value,
            Shares = shares.Select(s => new ComponentCreditShareResponse
            {
                HouseId = s.Participation.HouseId,
                HouseName = names.GetValueOrDefault(s.Participation.HouseId, s.Participation.HouseId),
                Weight = s.Weight,
                Amount = s.Amount,
            }).ToList(),
        };
    }

    // ───────────────────── Helpers ─────────────────────

    /// <summary>Validates the request and resolves house, meter, component and ownership period into an entity.</summary>
    private async Task<OpeningBalance> BuildAsync(SaveOpeningBalanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        var source = Clean(request.Source);
        if (source is null)
            errors.Add("Uveďte zdroj hodnoty (např. „odečet 22. 5. 2023, foto Jindra“).");
        if (request.Date == default)
            errors.Add("Datum je povinné.");
        ThrowIfAny(errors);

        var balance = new OpeningBalance
        {
            Type = request.Type,
            Date = request.Date,
            Value = request.Value,
            IsEstimate = request.IsEstimate,
            Source = source!,
            Note = Clean(request.Note),
        };

        switch (request.Type)
        {
            case OpeningBalanceType.MeterReading:
            {
                var meter = string.IsNullOrWhiteSpace(request.MeterId)
                    ? null
                    : await _meters.GetAsync(PartitionKeys.Meter, request.MeterId);
                if (meter is null)
                    throw new BusinessRuleException(["Vyberte vodoměr."]);
                if (request.Value < 0)
                    throw new BusinessRuleException(["Stav vodoměru nemůže být záporný."]);
                if (decimal.Round(request.Value, 3) != request.Value)
                    throw new BusinessRuleException(["Stav vodoměru může mít nejvýš tři desetinná místa (litry)."]);
                balance.MeterId = meter.Id;
                balance.HouseId = meter.HouseId;
                balance.OwnershipPeriodId = meter.HouseId is null ? null : await RequirePeriodAsync(meter.HouseId, request.Date);
                balance.Key = OpeningBalance.KeyFor(balance.Type, meter.Id, balance.OwnershipPeriodId);
                break;
            }
            case OpeningBalanceType.FundShare:
            {
                var house = string.IsNullOrWhiteSpace(request.HouseId)
                    ? null
                    : await _houses.GetAsync(PartitionKeys.House, request.HouseId);
                if (house is null)
                    throw new BusinessRuleException(["Vyberte dům."]);
                ThrowIfAny(CheckMoney(request.Value));
                balance.HouseId = house.Id;
                balance.OwnershipPeriodId = await RequirePeriodAsync(house.Id, request.Date);
                balance.Key = OpeningBalance.KeyFor(balance.Type, house.Id, balance.OwnershipPeriodId);
                break;
            }
            case OpeningBalanceType.ComponentCredit:
            {
                var component = string.IsNullOrWhiteSpace(request.ComponentId)
                    ? null
                    : await _components.GetAsync(PartitionKeys.CostComponent, request.ComponentId);
                if (component is null)
                    throw new BusinessRuleException(["Vyberte nákladovou složku."]);
                ThrowIfAny(CheckMoney(request.Value));
                balance.ComponentId = component.Id;
                balance.Key = OpeningBalance.KeyFor(balance.Type, component.Id, null);
                break;
            }
            default:
                throw new BusinessRuleException(["Neznámý typ počátečního stavu."]);
        }
        return balance;
    }

    private async Task<string> RequirePeriodAsync(string houseId, DateOnly date)
    {
        var periods = await _periods.GetByHouseAsync(houseId);
        var period = periods.FirstOrDefault(p => AllocationSegments.IsActive(p.ValidFrom, p.ValidTo, date))
            ?? throw new BusinessRuleException([$"Dům nemá k {Day(date)} období vlastnictví — nejdřív spusťte „Start účtování k datu“."]);
        return period.Id;
    }

    /// <summary>
    /// The opening meter state is also a meter reading on that day: create it, or update the reading this
    /// opening value created earlier. A different physical reading on that day is not overwritten.
    /// </summary>
    private async Task SyncMeterReadingAsync(OpeningBalance balance, AuditActor actor, OpeningBalance? previous)
    {
        if (balance.Type != OpeningBalanceType.MeterReading)
            return;

        var readingDate = PragueClock.AsUtcMidnight(balance.Date);
        var reading = await _readings.GetAsync(balance.MeterId!, Domain.Helpers.InvertedTimestamp.FromDateTime(readingDate));
        if (reading is not null)
        {
            // Same value: the opening state just refers to the existing reading.
            if (reading.Value == balance.Value && (previous is null || reading.IsEstimate == balance.IsEstimate))
                return;
            var ownedByOpening = previous is not null && reading.Value == previous.Value;
            if (!ownedByOpening)
                throw new BusinessRuleException([$"K {Day(balance.Date)} už existuje odečet {reading.Value.ToString("0.###", Czech)} m³ — použijte jeho hodnotu, nebo nejdřív opravte odečet."]);
        }

        var before = reading;
        var updated = new MeterReading
        {
            MeterId = balance.MeterId!,
            ReadingDate = readingDate,
            Value = balance.Value,
            Source = ReadingSource.Manual,
            ImportedAt = _clock.Now.UtcDateTime,
            ImportedBy = actor.UserId,
            IsEstimate = balance.IsEstimate,
            EstimateNote = balance.IsEstimate ? $"Počáteční stav: {balance.Source}" : null,
        };
        await _readings.UpsertAsync(updated);
        await _audit.LogAsync(MeterReadingEntity, $"{updated.MeterId}|{balance.Date:yyyy-MM-dd}",
            before is null ? AuditActions.Create : AuditActions.Update, before, updated, actor, "Počáteční stav vodoměru");
    }

    private static IReadOnlyList<string> CheckMoney(decimal value) =>
        decimal.Round(value, 2) != value ? ["Částka může mít nejvýš dvě desetinná místa."] : [];

    private static bool IsLocked(DateOnly date, DateOnly? lastClosed) => lastClosed is { } closed && date <= closed;

    private static void ThrowIfAny(IReadOnlyList<string> errors)
    {
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);

    private sealed record Names(
        IReadOnlyDictionary<string, string> Houses,
        IReadOnlyDictionary<string, string> Components,
        IReadOnlyDictionary<string, string> Meters);

    private async Task<Names> LoadNamesAsync() => new(
        (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name),
        (await _components.GetAllComponentsAsync()).ToDictionary(c => c.Id, c => c.Name),
        (await _meters.GetByPartitionKeyAsync(PartitionKeys.Meter)).ToDictionary(m => m.Id, m => m.MeterNumber));

    private static OpeningBalanceResponse ToResponse(OpeningBalance b, Names names, DateOnly? lastClosed) => new()
    {
        Key = b.Key,
        Type = b.Type,
        HouseId = b.HouseId,
        HouseName = b.HouseId is null ? null : names.Houses.GetValueOrDefault(b.HouseId, b.HouseId),
        ComponentId = b.ComponentId,
        ComponentName = b.ComponentId is null ? null : names.Components.GetValueOrDefault(b.ComponentId, b.ComponentId),
        MeterId = b.MeterId,
        MeterNumber = b.MeterId is null ? null : names.Meters.GetValueOrDefault(b.MeterId, b.MeterId),
        OwnershipPeriodId = b.OwnershipPeriodId,
        Date = b.Date,
        Value = b.Value,
        IsEstimate = b.IsEstimate,
        Source = b.Source,
        Note = b.Note,
        Locked = IsLocked(b.Date, lastClosed),
    };

    private static OwnershipPeriodResponse ToResponse(OwnershipPeriod p, string houseName) => new()
    {
        Id = p.Id,
        HouseId = p.HouseId,
        HouseName = houseName,
        OwnerName = p.OwnerName,
        Contact = p.Contact,
        ValidFrom = p.ValidFrom,
        ValidTo = p.ValidTo,
    };
}
