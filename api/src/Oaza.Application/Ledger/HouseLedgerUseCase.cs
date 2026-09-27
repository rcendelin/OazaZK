using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Application.Ledger;

/// <summary>Who asks — members see only their own house's detail (T07).</summary>
public record LedgerRequester(UserRole Role, string? HouseId);

/// <summary>
/// The house ledger (T07): opening fund share, payments of the house and its allocated costs with a running saldo
/// (positive = přeplatek, X1), every cost with its calculation. The overview puts all houses side by side per
/// component, with a control row that Σ houses equals what each component allocated.
/// </summary>
public class HouseLedgerUseCase
{
    private static readonly DateOnly DefaultStart = new(2023, 11, 1);

    private readonly LedgerCostCollector _collector;
    private readonly IHouseRepository _houses;
    private readonly IAdvancePaymentRepository _payments;
    private readonly IOpeningBalanceRepository _openingBalances;
    private readonly IOwnershipPeriodRepository _periods;
    private readonly ICostComponentRepository _components;
    private readonly IClock _clock;

    public HouseLedgerUseCase(
        LedgerCostCollector collector,
        IHouseRepository houses,
        IAdvancePaymentRepository payments,
        IOpeningBalanceRepository openingBalances,
        IOwnershipPeriodRepository periods,
        ICostComponentRepository components,
        IClock clock)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _openingBalances = openingBalances ?? throw new ArgumentNullException(nameof(openingBalances));
        _periods = periods ?? throw new ArgumentNullException(nameof(periods));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Ledger of one house for an ownership period (default: the current owner's) within <c>[from, to]</c>
    /// (default: the accounting start … today, clipped to the ownership period).
    /// </summary>
    public async Task<HouseLedgerResponse> GetHouseLedgerAsync(
        string houseId, string? ownershipPeriodId, DateOnly? from, DateOnly? to, LedgerRequester requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (requester.Role == UserRole.Member && requester.HouseId != houseId)
            throw new AppException("Detail cizího domu není dostupný.", 403);

        var house = await _houses.GetAsync(PartitionKeys.House, houseId) ?? throw new NotFoundException("House", houseId);
        var periods = (await _periods.GetByHouseAsync(houseId)).OrderBy(p => p.ValidFrom).ToList();
        var range = await RangeAsync(from, to);

        OwnershipPeriod? period;
        if (ownershipPeriodId is not null)
            period = periods.FirstOrDefault(p => p.Id == ownershipPeriodId) ?? throw new NotFoundException("OwnershipPeriod", ownershipPeriodId);
        else
            period = periods.LastOrDefault(p => p.ValidFrom <= range.To);

        if (period is not null)
        {
            var clipped = range.Intersect(new DateRange(period.ValidFrom, period.ValidTo ?? DateOnly.MaxValue.AddDays(-1)));
            range = clipped ?? new DateRange(period.ValidFrom, period.ValidFrom);
        }

        var items = new List<LedgerItemResponse>();
        foreach (var opening in (await _openingBalances.GetAllBalancesAsync())
                     .Where(o => o.Type == OpeningBalanceType.FundShare && o.HouseId == houseId
                                 && (period is null || o.OwnershipPeriodId == period.Id) && range.Contains(o.Date)))
        {
            items.Add(new LedgerItemResponse
            {
                Date = opening.Date,
                Kind = LedgerItemKind.Opening,
                Description = $"Počáteční podíl ve fondu ({opening.Source})",
                Amount = opening.Value,
            });
        }

        items.AddRange(PaymentItems(await _payments.GetByHouseIdAsync(houseId), range));

        var collected = await _collector.CollectAsync(range);
        items.AddRange(collected.Costs.Where(c => c.HouseId == houseId).Select(c => new LedgerItemResponse
        {
            Date = c.Date,
            Kind = c.Kind,
            Description = c.Description,
            ComponentId = c.ComponentId,
            ComponentName = c.ComponentName,
            Amount = -c.Amount,
            Detail = new LedgerDetailResponse
            {
                Total = c.Detail.Total,
                SegmentFrom = c.Detail.Segment.From,
                SegmentTo = c.Detail.Segment.To,
                SegmentDays = c.Detail.Segment.Days,
                SegmentAmount = c.Detail.SegmentAmount,
                Method = c.Detail.Method,
                Weight = c.Detail.Weight,
                TotalWeight = c.Detail.TotalWeight,
                Explanation = c.Detail.Explanation,
            },
        }));

        var ordered = items.OrderBy(i => i.Date).ThenBy(i => Rank(i.Kind)).ThenBy(i => i.ComponentName, StringComparer.CurrentCulture).ToList();
        var balance = 0m;
        foreach (var item in ordered)
        {
            balance += item.Amount;
            item.Balance = balance;
        }

        return new HouseLedgerResponse
        {
            HouseId = house.Id,
            HouseName = house.Name,
            From = range.From,
            To = range.To,
            OwnershipPeriod = period is null ? null : ToResponse(period),
            OwnershipPeriods = periods.Select(ToResponse).ToList(),
            Opening = ordered.Where(i => i.Kind == LedgerItemKind.Opening).Sum(i => i.Amount),
            Payments = ordered.Where(i => i.Kind is LedgerItemKind.Payment or LedgerItemKind.Payout).Sum(i => i.Amount),
            Costs = -ordered.Where(i => i.Kind is LedgerItemKind.Cost or LedgerItemKind.Credit or LedgerItemKind.Water or LedgerItemKind.Loss).Sum(i => i.Amount),
            Saldo = balance,
            Items = ordered,
        };
    }

    /// <summary>All houses × components for <c>[from, to]</c> with the control row. No personal data (T07).</summary>
    public async Task<LedgerOverviewResponse> GetOverviewAsync(DateOnly? from, DateOnly? to)
    {
        var range = await RangeAsync(from, to);
        var collected = await _collector.CollectAsync(range);
        var openings = await _openingBalances.GetAllBalancesAsync();
        var houses = (await _houses.GetByPartitionKeyAsync(PartitionKeys.House))
            .Where(h => h.IsActive || collected.Costs.Any(c => c.HouseId == h.Id))
            .OrderBy(h => h.Name, StringComparer.CurrentCulture)
            .ToList();

        var rows = new List<LedgerOverviewHouse>();
        foreach (var house in houses)
        {
            var opening = openings.Where(o => o.Type == OpeningBalanceType.FundShare && o.HouseId == house.Id && range.Contains(o.Date)).Sum(o => o.Value);
            var payments = PaymentItems(await _payments.GetByHouseIdAsync(house.Id), range).Sum(i => i.Amount);
            var costs = collected.Costs.Where(c => c.HouseId == house.Id)
                .GroupBy(c => c.ComponentId)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
            rows.Add(new LedgerOverviewHouse
            {
                HouseId = house.Id,
                HouseName = house.Name,
                Opening = opening,
                Payments = payments,
                Costs = costs,
                Saldo = opening + payments - costs.Values.Sum(),
            });
        }

        return new LedgerOverviewResponse
        {
            From = range.From,
            To = range.To,
            Components = collected.Controls.Select(c => new LedgerOverviewComponent
            {
                ComponentId = c.ComponentId,
                ComponentName = c.ComponentName,
                AllocatedTotal = c.AllocatedTotal,
                HousesTotal = rows.Sum(r => r.Costs.GetValueOrDefault(c.ComponentId)),
                Matches = c.AllocatedTotal == rows.Sum(r => r.Costs.GetValueOrDefault(c.ComponentId)),
                Warnings = c.Warnings.ToList(),
            }).ToList(),
            Houses = rows,
        };
    }

    /// <summary>
    /// Payments of the house in the range: advances on the first day of their month, extra payments and payouts on
    /// their payment day. The old opening-balance payment type is replaced by the fund share (T03, X2) and skipped.
    /// </summary>
    private static IEnumerable<LedgerItemResponse> PaymentItems(IEnumerable<AdvancePayment> payments, DateRange range)
    {
        foreach (var p in payments)
        {
            var (date, kind, amount, description) = p.Type switch
            {
                PaymentType.Advance => (new DateOnly(p.Year, p.Month, 1), LedgerItemKind.Payment, p.Amount, $"Záloha {p.Month}/{p.Year}"),
                PaymentType.Doplatek => (DateOnly.FromDateTime(p.PaymentDate), LedgerItemKind.Payment, p.Amount, "Doplatek"),
                PaymentType.Payout => (DateOnly.FromDateTime(p.PaymentDate), LedgerItemKind.Payout, -p.Amount, "Výplata přeplatku"),
                _ => (default(DateOnly), LedgerItemKind.Payment, 0m, string.Empty),
            };
            if (date == default || !range.Contains(date))
                continue;
            yield return new LedgerItemResponse
            {
                Date = date,
                Kind = kind,
                Description = p.Note is null ? description : $"{description} — {p.Note}",
                Amount = amount,
            };
        }
    }

    /// <summary>Order within a day: starting values first (fund share, supplier credit), then payments, then costs.</summary>
    private static int Rank(LedgerItemKind kind) => kind switch
    {
        LedgerItemKind.Opening => 0,
        LedgerItemKind.Credit => 1,
        LedgerItemKind.Payment => 2,
        LedgerItemKind.Payout => 3,
        LedgerItemKind.Cost => 4,
        LedgerItemKind.Water => 5,
        _ => 6,
    };

    private async Task<DateRange> RangeAsync(DateOnly? from, DateOnly? to)
    {
        var start = from ?? (await _components.GetAllComponentsAsync()).Select(c => c.StartDate).DefaultIfEmpty(DefaultStart).Min();
        var end = to ?? _clock.Today;
        if (start > end)
            throw new AppException("Datum od musí být nejpozději v den data do.");
        if (end.DayNumber - start.DayNumber > 3660)
            throw new AppException("Období může mít nejvýš 10 let.");
        return new DateRange(start, end);
    }

    private static LedgerPeriodResponse ToResponse(OwnershipPeriod p) => new()
    {
        Id = p.Id,
        OwnerName = p.OwnerName,
        ValidFrom = p.ValidFrom,
        ValidTo = p.ValidTo,
    };
}
