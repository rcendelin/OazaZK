using System.Globalization;
using Oaza.Application.Audit;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Application.Ledger;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Application.UseCases;

/// <summary>
/// „Převod domu“ (T03, R7): the new owner does not inherit the history. On the transfer day D:
/// (1) an interim closing of the house at D − 1 fixes the old owner's closing saldo, (2) the old ownership period
/// ends at D − 1, (3) a new one starts at D with its own opening values — the meter state at the handover and the
/// fund share (default 0). Costs from D belong to the new period.
/// </summary>
public class HouseTransferUseCase
{
    public const string HouseEntity = "House";

    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private readonly IHouseRepository _houses;
    private readonly IOwnershipPeriodRepository _periods;
    private readonly IWaterMeterRepository _meters;
    private readonly IMeterReadingRepository _readings;
    private readonly IClosingBoundary _closingBoundary;
    private readonly HouseLedgerUseCase _ledger;
    private readonly InterimClosingsUseCase _closings;
    private readonly OpeningBalancesUseCase _openingBalances;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public HouseTransferUseCase(
        IHouseRepository houses,
        IOwnershipPeriodRepository periods,
        IWaterMeterRepository meters,
        IMeterReadingRepository readings,
        IClosingBoundary closingBoundary,
        HouseLedgerUseCase ledger,
        InterimClosingsUseCase closings,
        OpeningBalancesUseCase openingBalances,
        IAuditLogger audit,
        IClock clock)
    {
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
        _periods = periods ?? throw new ArgumentNullException(nameof(periods));
        _meters = meters ?? throw new ArgumentNullException(nameof(meters));
        _readings = readings ?? throw new ArgumentNullException(nameof(readings));
        _closingBoundary = closingBoundary ?? throw new ArgumentNullException(nameof(closingBoundary));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _closings = closings ?? throw new ArgumentNullException(nameof(closings));
        _openingBalances = openingBalances ?? throw new ArgumentNullException(nameof(openingBalances));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>What the transfer will do: the old owner's closing saldo, the meter suggestion and any blocker.</summary>
    public async Task<HouseTransferPreviewResponse> PreviewAsync(string houseId, DateOnly transferDate)
    {
        var house = await _houses.GetAsync(PartitionKeys.House, houseId) ?? throw new NotFoundException(HouseEntity, houseId);
        var closingDate = transferDate.AddDays(-1);
        var preview = new HouseTransferPreviewResponse
        {
            HouseId = house.Id,
            HouseName = house.Name,
            TransferDate = transferDate,
            ClosingDate = closingDate,
        };

        var current = (await _periods.GetByHouseAsync(houseId)).FirstOrDefault(p => AllocationSegments.IsActive(p.ValidFrom, p.ValidTo, closingDate));
        preview.CurrentOwnerName = current?.OwnerName;
        preview.CurrentOwnerFrom = current?.ValidFrom;
        preview.Problems.AddRange(await ProblemsAsync(houseId, transferDate, current));

        if (current is not null && closingDate >= current.ValidFrom)
        {
            var ledger = await _ledger.GetHouseLedgerAsync(houseId, current.Id, null, closingDate, new LedgerRequester(UserRole.Admin, null));
            preview.ClosingSaldo = ledger.Saldo;
            preview.ClosingPayments = ledger.Payments;
            preview.ClosingCosts = ledger.Costs;
        }

        var meter = (await _meters.GetByHouseIdAsync(houseId)).FirstOrDefault(m => m.Type == MeterType.Individual);
        if (meter is not null)
        {
            preview.MeterId = meter.Id;
            preview.MeterNumber = meter.MeterNumber;
            var estimate = ReadingEstimator.Estimate(await _readings.GetByMeterIdAsync(meter.Id), transferDate.ToDateTime(TimeOnly.MinValue));
            preview.SuggestedMeterValue = estimate.Value;
            preview.SuggestedMeterNote = estimate.Note;
        }
        return preview;
    }

    public async Task<HouseTransferResponse> TransferAsync(string houseId, HouseTransferRequest request, AuditActor actor)
    {
        ArgumentNullException.ThrowIfNull(request);
        var house = await _houses.GetAsync(PartitionKeys.House, houseId) ?? throw new NotFoundException(HouseEntity, houseId);
        var closingDate = request.TransferDate.AddDays(-1);
        var periods = await _periods.GetByHouseAsync(houseId);
        var current = periods.FirstOrDefault(p => AllocationSegments.IsActive(p.ValidFrom, p.ValidTo, closingDate));
        var meter = (await _meters.GetByHouseIdAsync(houseId)).FirstOrDefault(m => m.Type == MeterType.Individual);

        var errors = (await ProblemsAsync(houseId, request.TransferDate, current)).ToList();
        var newOwner = request.NewOwnerName?.Trim() ?? string.Empty;
        if (newOwner.Length == 0)
            errors.Add("Zadejte jméno nového vlastníka.");
        if (meter is not null && request.MeterValue is null)
            errors.Add($"Zadejte stav vodoměru {meter.MeterNumber} při předání.");
        if (decimal.Round(request.FundShare, 2) != request.FundShare)
            errors.Add("Podíl ve fondu může mít nejvýš dvě desetinná místa.");
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? $"Převod domu na {newOwner} k {Day(request.TransferDate)}" : request.Reason.Trim();

        // 1. Interim closing of the house at D − 1 — the old owner's closing saldo.
        var closing = await _closings.CreateAsync(new CreateInterimClosingRequest
        {
            Date = closingDate, Scope = ClosingScope.House, HouseId = houseId, Reason = reason,
        }, actor);

        // 2. End the old ownership period.
        var before = Copy(current!);
        current!.ValidTo = closingDate;
        await _periods.UpsertAsync(current);
        await _audit.LogAsync(OpeningBalancesUseCase.OwnershipPeriodEntity, current.Id, AuditActions.Update, before, current, actor, reason);

        // 3. The new owner's period and opening values.
        var period = new OwnershipPeriod
        {
            Id = OwnershipPeriod.KeyFor(houseId, request.TransferDate),
            HouseId = houseId,
            OwnerName = newOwner,
            Contact = string.IsNullOrWhiteSpace(request.NewOwnerContact) ? null : request.NewOwnerContact.Trim(),
            ValidFrom = request.TransferDate,
        };
        await _periods.UpsertAsync(period);
        await _audit.LogAsync(OpeningBalancesUseCase.OwnershipPeriodEntity, period.Id, AuditActions.Create, null, period, actor, reason);

        await _openingBalances.CreateAsync(new SaveOpeningBalanceRequest
        {
            Type = OpeningBalanceType.FundShare, HouseId = houseId, Date = request.TransferDate, Value = request.FundShare,
            Source = request.FundShare == 0 ? "Převod domu — nový vlastník začíná s nulovým podílem" : "Převod domu — počáteční vklad nového vlastníka",
        }, actor);
        if (meter is not null)
        {
            await _openingBalances.CreateAsync(new SaveOpeningBalanceRequest
            {
                Type = OpeningBalanceType.MeterReading, MeterId = meter.Id, Date = request.TransferDate, Value = request.MeterValue!.Value,
                IsEstimate = request.MeterIsEstimate,
                Source = string.IsNullOrWhiteSpace(request.MeterSource) ? "Stav při předání domu" : request.MeterSource.Trim(),
            }, actor);
        }

        if (request.UpdateHouseContact)
        {
            var houseBefore = new House { Id = house.Id, Name = house.Name, Address = house.Address, ContactPerson = house.ContactPerson, Email = house.Email, IsActive = house.IsActive, DissolveOverpayment = house.DissolveOverpayment };
            house.ContactPerson = newOwner;
            house.Email = period.Contact ?? string.Empty;
            await _houses.UpsertAsync(house);
            await _audit.LogAsync(HouseEntity, house.Id, AuditActions.Update, houseBefore, house, actor, reason);
        }

        return new HouseTransferResponse { ClosingId = closing.Id, ClosingSaldo = closing.TotalSaldo, NewOwnershipPeriodId = period.Id };
    }

    private async Task<IReadOnlyList<string>> ProblemsAsync(string houseId, DateOnly transferDate, OwnershipPeriod? current)
    {
        var problems = new List<string>();
        var closingDate = transferDate.AddDays(-1);
        if (transferDate > _clock.Today)
            problems.Add("Převod lze zadat nejpozději k dnešku (mezizávěrka starého vlastníka je ke dni před předáním).");
        if (current is null)
            problems.Add($"Dům nemá k {Day(closingDate)} období vlastnictví — nejdřív spusťte „Start účtování k datu“.");
        else if (current.ValidTo is not null)
            problems.Add($"Období vlastnictví {current.OwnerName} už končí {Day(current.ValidTo.Value)}.");
        else if (current.ValidFrom >= transferDate)
            problems.Add($"Převod musí být po začátku vlastnictví {current.OwnerName} ({Day(current.ValidFrom)}).");
        if (await _closingBoundary.GetLastClosedDayAsync(houseId) is { } closed && closingDate <= closed)
            problems.Add($"Období je už uzavřené mezizávěrkou do {Day(closed)} — převod musí být později.");
        return problems;
    }

    private static OwnershipPeriod Copy(OwnershipPeriod p) => new()
    {
        Id = p.Id, HouseId = p.HouseId, OwnerName = p.OwnerName, Contact = p.Contact, ValidFrom = p.ValidFrom, ValidTo = p.ValidTo,
    };

    private static string Day(DateOnly day) => day.ToString("d. M. yyyy", CultureInfo.InvariantCulture);
}
