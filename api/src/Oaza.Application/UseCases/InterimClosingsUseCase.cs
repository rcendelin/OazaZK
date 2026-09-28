using System.Globalization;
using System.Text.Json;
using Oaza.Application.Audit;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Application.Ledger;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Time;

namespace Oaza.Application.UseCases;

/// <summary>
/// Interim closings (T08): a cut at a date that fixes the saldo of all houses, or of one house (a sale). The saldo is
/// snapshotted; from then on nothing up to the date may change and corrections are booked into the open period. The
/// annual closing is a closing of all houses at 31. 12.
/// </summary>
public class InterimClosingsUseCase
{
    public const string InterimClosingEntity = "InterimClosing";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IInterimClosingRepository _closings;
    private readonly IHouseRepository _houses;
    private readonly HouseLedgerUseCase _ledger;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public InterimClosingsUseCase(IInterimClosingRepository closings, IHouseRepository houses, HouseLedgerUseCase ledger, IAuditLogger audit, IClock clock)
    {
        _closings = closings ?? throw new ArgumentNullException(nameof(closings));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<InterimClosingResponse>> ListAsync()
    {
        var closings = await _closings.GetAllClosingsAsync();
        var names = await HouseNamesAsync();
        var latest = Latest(closings);
        return closings.OrderByDescending(c => c.Date).ThenBy(c => c.Scope)
            .Select(c => ToResponse(c, names, latest))
            .ToList();
    }

    /// <summary>A closing with its snapshot and the saldo at its date recomputed now (should not differ).</summary>
    public async Task<InterimClosingResponse> GetAsync(string id)
    {
        var closing = await _closings.GetAsync(PartitionKeys.InterimClosing, id) ?? throw new NotFoundException(InterimClosingEntity, id);
        var names = await HouseNamesAsync();
        var response = ToResponse(closing, names, Latest(await _closings.GetAllClosingsAsync()));
        var current = await SnapshotAsync(closing.Date, closing.Scope, closing.HouseId);
        response.Houses = Snapshot(closing).Select(row =>
        {
            var now = current.FirstOrDefault(c => c.HouseId == row.HouseId)?.Saldo ?? 0m;
            return new ClosingHouseResponse
            {
                HouseId = row.HouseId, HouseName = row.HouseName, Opening = row.Opening, Payments = row.Payments,
                Costs = row.Costs, Saldo = row.Saldo, CurrentSaldo = now, Difference = now - row.Saldo,
            };
        }).ToList();
        return response;
    }

    public async Task<InterimClosingResponse> CreateAsync(CreateInterimClosingRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var errors = new List<string>();
        if (reason is null)
            errors.Add("Uveďte důvod mezizávěrky (např. „roční závěrka 2026“, „prodej domu“).");
        if (request.Date == default)
            errors.Add("Datum mezizávěrky je povinné.");
        else if (request.Date >= _clock.Today)
            errors.Add("Mezizávěrku lze udělat nejpozději ke včerejšku.");
        House? house = null;
        if (request.Scope == ClosingScope.House)
        {
            house = string.IsNullOrWhiteSpace(request.HouseId) ? null : await _houses.GetAsync(PartitionKeys.House, request.HouseId);
            if (house is null)
                errors.Add("Vyberte dům.");
        }
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);

        var closings = await _closings.GetAllClosingsAsync();
        var relevant = request.Scope == ClosingScope.All
            ? closings
            : closings.Where(c => c.Scope == ClosingScope.All || c.HouseId == house!.Id).ToList();
        if (relevant.Count > 0 && relevant.Max(c => c.Date) >= request.Date)
            throw new BusinessRuleException([$"Období je už uzavřené do {Day(relevant.Max(c => c.Date))}; nová mezizávěrka musí být pozdější."]);

        var snapshot = await SnapshotAsync(request.Date, request.Scope, house?.Id);
        var closing = new InterimClosing
        {
            Id = InterimClosing.KeyFor(request.Date, request.Scope, house?.Id),
            Date = request.Date,
            Scope = request.Scope,
            HouseId = house?.Id,
            Reason = reason!,
            CreatedBy = actor.UserId,
            CreatedByName = actor.UserName,
            CreatedAt = _clock.Now.UtcDateTime,
            SnapshotJson = JsonSerializer.Serialize(snapshot, Json),
        };
        await _closings.UpsertAsync(closing);
        await _audit.LogAsync(InterimClosingEntity, closing.Id, AuditActions.Create, null, closing, actor, reason);

        var names = await HouseNamesAsync();
        return ToResponse(closing, names, closing.Id);
    }

    /// <summary>Removes the latest closing (a mistake). Older closings stay fixed.</summary>
    public async Task DeleteAsync(string id, string? reason, AuditActor actor)
    {
        var closing = await _closings.GetAsync(PartitionKeys.InterimClosing, id) ?? throw new NotFoundException(InterimClosingEntity, id);
        if (Latest(await _closings.GetAllClosingsAsync()) != closing.Id)
            throw new BusinessRuleException(["Zrušit lze jen poslední mezizávěrku."]);
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException(["Uveďte důvod zrušení mezizávěrky."]);

        await _closings.DeleteAsync(PartitionKeys.InterimClosing, id);
        await _audit.LogAsync(InterimClosingEntity, id, AuditActions.Delete, closing, null, actor, reason.Trim());
    }

    /// <summary>
    /// Export for the accountant (T08): the overview of all houses for the closing's calendar year up to its date
    /// (or the house's ledger for a house closing). The off-book fund (T10) is never part of the ledger, so it is not here.
    /// </summary>
    public async Task<ExportFile> ExportAsync(string id, string format)
    {
        var closing = await _closings.GetAsync(PartitionKeys.InterimClosing, id) ?? throw new NotFoundException(InterimClosingEntity, id);
        if (closing.Scope == ClosingScope.House)
        {
            var ledger = await _ledger.GetHouseLedgerAsync(closing.HouseId!, null, null, closing.Date, new LedgerRequester(UserRole.Admin, null));
            return LedgerExport.House(ledger, format);
        }

        var yearStart = new DateOnly(closing.Date.Year, 1, 1);
        var overview = await _ledger.GetOverviewAsync(yearStart, closing.Date);
        var file = LedgerExport.Overview(overview, format);
        var annual = closing.Date.Month == 12 && closing.Date.Day == 31;
        var name = annual ? $"rocni-zaverka-{closing.Date.Year}" : $"mezizaverka-{closing.Date:yyyyMMdd}";
        return file with { FileName = name + Path.GetExtension(file.FileName) };
    }

    /// <summary>The houses' saldo at <paramref name="date"/>: every house for All, the owner at that day for House.</summary>
    private async Task<List<ClosingSnapshotRow>> SnapshotAsync(DateOnly date, ClosingScope scope, string? houseId)
    {
        if (scope == ClosingScope.House)
        {
            var ledger = await _ledger.GetHouseLedgerAsync(houseId!, null, null, date, new LedgerRequester(UserRole.Admin, null));
            return [new ClosingSnapshotRow { HouseId = ledger.HouseId, HouseName = ledger.HouseName, Opening = ledger.Opening, Payments = ledger.Payments, Costs = ledger.Costs, Saldo = ledger.Saldo }];
        }

        var overview = await _ledger.GetOverviewAsync(null, date);
        return overview.Houses.Select(h => new ClosingSnapshotRow
        {
            HouseId = h.HouseId, HouseName = h.HouseName, Opening = h.Opening, Payments = h.Payments,
            Costs = h.Costs.Values.Sum(), Saldo = h.Saldo,
        }).ToList();
    }

    private static List<ClosingSnapshotRow> Snapshot(InterimClosing closing) =>
        JsonSerializer.Deserialize<List<ClosingSnapshotRow>>(closing.SnapshotJson, Json) ?? [];

    private static string? Latest(IReadOnlyList<InterimClosing> closings) =>
        closings.OrderByDescending(c => c.Date).ThenByDescending(c => c.CreatedAt).FirstOrDefault()?.Id;

    private async Task<Dictionary<string, string>> HouseNamesAsync() =>
        (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name);

    private static InterimClosingResponse ToResponse(InterimClosing c, IReadOnlyDictionary<string, string> names, string? latest) => new()
    {
        Id = c.Id,
        Date = c.Date,
        Scope = c.Scope,
        HouseId = c.HouseId,
        HouseName = c.HouseId is null ? null : names.GetValueOrDefault(c.HouseId, c.HouseId),
        Reason = c.Reason,
        CreatedByName = c.CreatedByName,
        CreatedAt = c.CreatedAt,
        TotalSaldo = Snapshot(c).Sum(r => r.Saldo),
        CanDelete = c.Id == latest,
    };

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);
}
