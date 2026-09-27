using System.Text.RegularExpressions;
using Oaza.Application.Audit;
using Oaza.Application.Deployment;
using Oaza.Application.Exceptions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Domain.Services;
using Oaza.Domain.Time;

namespace Oaza.Application.OffBookFunds;

/// <summary>
/// The off-book fund (T10, O4) — evidence of an informal fund outside the association's accounting (e.g. „Fond na
/// ohňostroje“), money on the manager's private account. Strictly separate: only this module uses
/// <see cref="IOffBookFundRepository"/>, so the fund never appears in the house saldo, interim closings, the cash
/// book or any export for the accountant. Behind the feature flag <c>OFF_BOOK_FUND_ENABLED</c> — off, everything 404s.
/// </summary>
public partial class OffBookFundUseCase
{
    public const string FundEntity = "OffBookFund";
    public const string RecordEntity = "OffBookFundRecord";

    private readonly IOffBookFundRepository _repository;
    private readonly IHouseRepository _houses;
    private readonly FeatureFlags _flags;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;

    public OffBookFundUseCase(IOffBookFundRepository repository, IHouseRepository houses, FeatureFlags flags, IAuditLogger audit, IClock clock)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _houses = houses ?? throw new ArgumentNullException(nameof(houses));
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<OffBookFundResponse>> ListAsync()
    {
        EnsureEnabled();
        var result = new List<OffBookFundResponse>();
        foreach (var fund in (await _repository.GetFundsAsync()).OrderBy(f => f.Name, StringComparer.CurrentCulture))
            result.Add(ToResponse(fund, await _repository.GetRecordsAsync(fund.Id)));
        return result;
    }

    public async Task<OffBookFundDetailResponse> GetAsync(string fundId)
    {
        EnsureEnabled();
        var fund = await GetFundAsync(fundId);
        var records = await _repository.GetRecordsAsync(fundId);
        var names = await HouseNamesAsync();
        return new OffBookFundDetailResponse
        {
            Fund = ToResponse(fund, records),
            Calls = records.Where(r => r.Kind == FundRecordKind.Call).OrderByDescending(r => r.Date).Select(call =>
            {
                var status = OffBookFundCalculator.CallStatus(call, records);
                return new FundCallResponse
                {
                    Id = call.Id, Date = call.Date, DueDate = call.DueDate, AmountPerHouse = call.Amount, Text = call.Text,
                    Collected = status.Sum(s => s.Paid), Debtors = status.Count(s => !s.IsPaid),
                    Houses = status.Select(s => new FundCallHouseResponse
                    {
                        HouseId = s.HouseId, HouseName = names.GetValueOrDefault(s.HouseId, s.HouseId), Expected = s.Expected, Paid = s.Paid, IsPaid = s.IsPaid,
                    }).OrderBy(h => h.HouseName, StringComparer.CurrentCulture).ToList(),
                };
            }).ToList(),
            Records = records.Where(r => r.Kind != FundRecordKind.Call)
                .OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt)
                .Select(r => new FundRecordResponse
                {
                    Id = r.Id, Kind = r.Kind, Date = r.Date, Amount = r.Amount, Text = r.Text,
                    HouseName = r.HouseId is null ? null : names.GetValueOrDefault(r.HouseId, r.HouseId),
                    CallId = r.CallId, Method = r.Method, PaidBy = r.PaidBy, HasReceipt = r.HasReceipt, ExpenseId = r.ExpenseId, PaidTo = r.PaidTo,
                }).ToList(),
        };
    }

    public async Task<OffBookFundResponse> CreateAsync(SaveOffBookFundRequest request, AuditActor actor)
    {
        EnsureEnabled();
        var fund = new OffBookFund { Id = Guid.NewGuid().ToString() };
        Apply(fund, request);
        await _repository.UpsertFundAsync(fund);
        await _audit.LogAsync(FundEntity, fund.Id, AuditActions.Create, null, fund, actor);
        return ToResponse(fund, []);
    }

    public async Task<OffBookFundResponse> UpdateAsync(string fundId, SaveOffBookFundRequest request, AuditActor actor)
    {
        EnsureEnabled();
        var fund = await GetFundAsync(fundId);
        var before = new OffBookFund { Id = fund.Id, Name = fund.Name, Purpose = fund.Purpose, ManagerName = fund.ManagerName, AccountDescription = fund.AccountDescription, Active = fund.Active };
        Apply(fund, request);
        await _repository.UpsertFundAsync(fund);
        await _audit.LogAsync(FundEntity, fund.Id, AuditActions.Update, before, fund, actor);
        return ToResponse(fund, await _repository.GetRecordsAsync(fundId));
    }

    /// <summary>Adds a call, contribution, expense or settlement.</summary>
    public async Task<FundRecordResponse> AddRecordAsync(string fundId, AddFundRecordRequest request, AuditActor actor)
    {
        EnsureEnabled();
        ArgumentNullException.ThrowIfNull(request);
        await GetFundAsync(fundId);
        var records = await _repository.GetRecordsAsync(fundId);
        var houses = await HouseNamesAsync();
        var errors = new List<string>();
        if (request.Date == default)
            errors.Add("Datum je povinné.");
        else if (request.Date > _clock.Today)
            errors.Add("Datum nemůže být v budoucnosti.");
        if (request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount)
            errors.Add("Částka musí být kladná, nejvýš na haléře.");

        var record = new FundRecord
        {
            Id = Guid.NewGuid().ToString(), FundId = fundId, Kind = request.Kind, Date = request.Date, Amount = request.Amount,
            Text = request.Text?.Trim() ?? string.Empty, CreatedBy = actor.UserId, CreatedAt = _clock.Now.UtcDateTime,
        };
        switch (request.Kind)
        {
            case FundRecordKind.Call:
                record.DueDate = request.DueDate;
                record.HouseIds = request.HouseIds.Distinct().ToList();
                if (record.HouseIds.Count == 0)
                    errors.Add("Vyberte domy, kterých se výzva týká.");
                if (record.HouseIds.Any(h => !houses.ContainsKey(h)))
                    errors.Add("Některý z vybraných domů neexistuje.");
                break;
            case FundRecordKind.Contribution:
                record.HouseId = request.HouseId;
                record.CallId = string.IsNullOrWhiteSpace(request.CallId) ? null : request.CallId;
                record.Method = string.IsNullOrWhiteSpace(request.Method) ? "převod" : request.Method.Trim();
                if (request.HouseId is null || !houses.ContainsKey(request.HouseId))
                    errors.Add("Vyberte dům, který přispěl.");
                if (record.CallId is not null && !records.Any(r => r.Kind == FundRecordKind.Call && r.Id == record.CallId && r.HouseIds.Contains(request.HouseId ?? string.Empty)))
                    errors.Add("Dům není ve vybrané výzvě.");
                break;
            case FundRecordKind.Expense:
                record.PaidBy = string.IsNullOrWhiteSpace(request.PaidBy) ? null : request.PaidBy.Trim();
                record.HasReceipt = request.HasReceipt;
                if (record.Text.Length == 0)
                    errors.Add("Uveďte, za co se platilo.");
                if (record.PaidBy is null && request.Amount > OffBookFundCalculator.Balance(records))
                    errors.Add("Ve fondu není dost peněz — zadejte, kdo výdaj zaplatil předem.");
                break;
            case FundRecordKind.Settlement:
                record.ExpenseId = request.ExpenseId;
                var expense = records.FirstOrDefault(r => r.Kind == FundRecordKind.Expense && r.Id == request.ExpenseId && r.PaidBy is not null);
                if (expense is null)
                    errors.Add("Vyberte výdaj zaplacený předem, který se vyrovnává.");
                else
                {
                    record.PaidTo = expense.PaidBy;
                    var settled = records.Where(r => r.Kind == FundRecordKind.Settlement && r.ExpenseId == expense.Id).Sum(r => r.Amount);
                    if (settled + request.Amount > expense.Amount)
                        errors.Add("Vyrovnání je vyšší než zbývající dluh vůči tomu, kdo platil.");
                }
                if (request.Amount > OffBookFundCalculator.Balance(records))
                    errors.Add("Ve fondu není dost peněz na vyrovnání.");
                break;
            default:
                errors.Add("Neznámý druh záznamu.");
                break;
        }
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);

        await _repository.UpsertRecordAsync(record);
        await _audit.LogAsync(RecordEntity, record.Id, AuditActions.Create, null, record, actor);
        return new FundRecordResponse
        {
            Id = record.Id, Kind = record.Kind, Date = record.Date, Amount = record.Amount, Text = record.Text,
            HouseName = record.HouseId is null ? null : houses.GetValueOrDefault(record.HouseId), CallId = record.CallId, Method = record.Method,
            PaidBy = record.PaidBy, HasReceipt = record.HasReceipt, ExpenseId = record.ExpenseId, PaidTo = record.PaidTo,
        };
    }

    /// <summary>Removes a record entered by mistake (the fund is informal evidence); a call with contributions stays.</summary>
    public async Task DeleteRecordAsync(string fundId, string recordId, string? reason, AuditActor actor)
    {
        EnsureEnabled();
        var record = await _repository.GetRecordAsync(fundId, recordId) ?? throw new NotFoundException(RecordEntity, recordId);
        var records = await _repository.GetRecordsAsync(fundId);
        if (record.Kind == FundRecordKind.Call && records.Any(r => r.CallId == recordId))
            throw new BusinessRuleException(["Výzvu s příspěvky nelze smazat."]);
        if (record.Kind == FundRecordKind.Expense && records.Any(r => r.ExpenseId == recordId))
            throw new BusinessRuleException(["Výdaj s vyrovnáním nelze smazat."]);
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessRuleException(["Uveďte důvod smazání."]);

        await _repository.DeleteRecordAsync(fundId, recordId);
        await _audit.LogAsync(RecordEntity, recordId, AuditActions.Delete, record, null, actor, reason.Trim());
    }

    private void EnsureEnabled()
    {
        if (!_flags.OffBookFundEnabled)
            throw new AppException("Požadovaný záznam nebyl nalezen.", 404);
    }

    private async Task<OffBookFund> GetFundAsync(string fundId) =>
        await _repository.GetFundAsync(fundId) ?? throw new NotFoundException(FundEntity, fundId);

    private async Task<Dictionary<string, string>> HouseNamesAsync() =>
        (await _houses.GetByPartitionKeyAsync(PartitionKeys.House)).ToDictionary(h => h.Id, h => h.Name);

    private static void Apply(OffBookFund fund, SaveOffBookFundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Název fondu je povinný.");
        if (string.IsNullOrWhiteSpace(request.ManagerName))
            errors.Add("Uveďte správce fondu.");
        if (request.AccountDescription is not null && AccountNumber().IsMatch(request.AccountDescription))
            errors.Add("Neukládejte celé číslo účtu — popište jen, kde peníze jsou (např. „soukromý účet správce“).");
        if (errors.Count > 0)
            throw new BusinessRuleException(errors);

        fund.Name = request.Name.Trim();
        fund.Purpose = string.IsNullOrWhiteSpace(request.Purpose) ? null : request.Purpose.Trim();
        fund.ManagerName = request.ManagerName.Trim();
        fund.AccountDescription = string.IsNullOrWhiteSpace(request.AccountDescription) ? null : request.AccountDescription.Trim();
        fund.Active = request.Active;
    }

    private static OffBookFundResponse ToResponse(OffBookFund fund, IReadOnlyList<FundRecord> records) => new()
    {
        Id = fund.Id, Name = fund.Name, Purpose = fund.Purpose, ManagerName = fund.ManagerName,
        AccountDescription = fund.AccountDescription, Active = fund.Active,
        Balance = OffBookFundCalculator.Balance(records), Outstanding = OffBookFundCalculator.Outstanding(records),
    };

    /// <summary>A Czech account number (optional prefix, 2–10 digits, bank code) or an IBAN.</summary>
    [GeneratedRegex(@"(\d{0,6}-?\d{6,10}/\d{4})|([A-Z]{2}\d{2}[ ]?\d{4})")]
    private static partial Regex AccountNumber();
}
