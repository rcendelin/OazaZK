using Oaza.Application.Interfaces;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.TestSupport;

public sealed class ClosedUntil(DateOnly? day) : IClosingBoundary
{
    public DateOnly? Day { get; set; } = day;
    public Task<DateOnly?> GetLastClosedDayAsync(string? houseId = null) => Task.FromResult(Day);
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class MemoryInterimClosings() : MemoryRepo<InterimClosing>(_ => PartitionKeys.InterimClosing, c => c.Id), IInterimClosingRepository
{
    public Task<IReadOnlyList<InterimClosing>> GetAllClosingsAsync() => GetByPartitionKeyAsync(PartitionKeys.InterimClosing);
}

public sealed class MemoryCashBook() : MemoryRepo<CashBookEntry>(_ => PartitionKeys.CashBook, e => e.Id), ICashBookRepository
{
    public Task<IReadOnlyList<CashBookEntry>> GetAllEntriesAsync() => GetByPartitionKeyAsync(PartitionKeys.CashBook);
}

public sealed class MemoryOffBookFunds : IOffBookFundRepository
{
    public readonly Dictionary<string, OffBookFund> Funds = new();
    public readonly Dictionary<(string, string), FundRecord> Records = new();

    public Task<IReadOnlyList<OffBookFund>> GetFundsAsync() => Task.FromResult<IReadOnlyList<OffBookFund>>(Funds.Values.ToList());
    public Task<OffBookFund?> GetFundAsync(string fundId) => Task.FromResult(Funds.GetValueOrDefault(fundId));
    public Task UpsertFundAsync(OffBookFund fund) { Funds[fund.Id] = fund; return Task.CompletedTask; }
    public Task<IReadOnlyList<FundRecord>> GetRecordsAsync(string fundId) =>
        Task.FromResult<IReadOnlyList<FundRecord>>(Records.Values.Where(r => r.FundId == fundId).ToList());
    public Task<FundRecord?> GetRecordAsync(string fundId, string recordId) => Task.FromResult(Records.GetValueOrDefault((fundId, recordId)));
    public Task UpsertRecordAsync(FundRecord record) { Records[(record.FundId, record.Id)] = record; return Task.CompletedTask; }
    public Task DeleteRecordAsync(string fundId, string recordId) { Records.Remove((fundId, recordId)); return Task.CompletedTask; }
}

/// <summary>Prescribed advances over an empty new model — only the admin overrides matter.</summary>
public static class PrescribedAdvances
{
    public static Oaza.Application.UseCases.CalculatePrescribedAdvancesUseCase WithoutCosts(IAdvanceSettingsRepository settings, IHouseRepository houses) =>
        new(settings, houses, new MemoryComponents(),
            new Oaza.Application.Ledger.LedgerCostCollector(new MemoryComponents(), new MemoryRules(), new MemoryParticipations(),
                new MemoryCostEntries(), new MemoryOpeningBalances(), new MemoryMeters(), new MemoryReadings(), houses),
            new Oaza.Domain.Time.PragueClock(new FixedTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero))));
}
