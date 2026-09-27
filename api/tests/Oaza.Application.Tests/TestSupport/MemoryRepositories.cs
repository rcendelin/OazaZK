using Oaza.Application.Interfaces;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.TestSupport;

/// <summary>In-memory table: (PK, RK) → entity. For stateful use case tests where mocks get unwieldy.</summary>
public class MemoryRepo<T>(Func<T, string> pk, Func<T, string> rk) : IRepository<T> where T : class
{
    public readonly Dictionary<(string, string), T> Items = new();

    public Task<T?> GetAsync(string partitionKey, string rowKey) => Task.FromResult(Items.GetValueOrDefault((partitionKey, rowKey)));

    public Task<IReadOnlyList<T>> GetByPartitionKeyAsync(string partitionKey) =>
        Task.FromResult<IReadOnlyList<T>>(Items.Where(i => i.Key.Item1 == partitionKey).Select(i => i.Value).ToList());

    public Task<IReadOnlyList<T>> GetAllAsync() => Task.FromResult<IReadOnlyList<T>>(Items.Values.ToList());

    public Task UpsertAsync(T entity)
    {
        Items[(pk(entity), rk(entity))] = entity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string partitionKey, string rowKey)
    {
        Items.Remove((partitionKey, rowKey));
        return Task.CompletedTask;
    }
}

public sealed class MemoryComponents() : MemoryRepo<CostComponent>(_ => PartitionKeys.CostComponent, c => c.Id), ICostComponentRepository
{
    public Task<IReadOnlyList<CostComponent>> GetAllComponentsAsync() => GetByPartitionKeyAsync(PartitionKeys.CostComponent);
}

public sealed class MemoryRules() : MemoryRepo<ComponentAllocationRule>(r => r.ComponentId, r => r.Id), IComponentAllocationRuleRepository
{
    public Task<IReadOnlyList<ComponentAllocationRule>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
}

public sealed class MemoryParticipations() : MemoryRepo<Participation>(p => p.ComponentId, p => p.Id), IParticipationRepository
{
    public Task<IReadOnlyList<Participation>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
}

public sealed class MemoryHouses() : MemoryRepo<House>(_ => PartitionKeys.House, h => h.Id), IHouseRepository;

public sealed class MemoryMeters() : MemoryRepo<WaterMeter>(_ => PartitionKeys.Meter, m => m.Id), IWaterMeterRepository
{
    public Task<IReadOnlyList<WaterMeter>> GetByHouseIdAsync(string houseId) =>
        Task.FromResult<IReadOnlyList<WaterMeter>>(Items.Values.Where(m => m.HouseId == houseId).ToList());
}

public sealed class MemoryReadings() : MemoryRepo<MeterReading>(r => r.MeterId, r => InvertedTimestamp.FromDateTime(r.ReadingDate)), IMeterReadingRepository
{
    public Task<IReadOnlyList<MeterReading>> GetByMeterIdAsync(string meterId) => GetByPartitionKeyAsync(meterId);

    public Task<IReadOnlyList<MeterReading>> GetLatestByMeterIdAsync(string meterId, int count) =>
        Task.FromResult<IReadOnlyList<MeterReading>>(Items.Values.Where(r => r.MeterId == meterId).OrderByDescending(r => r.ReadingDate).Take(count).ToList());
}

public sealed class MemoryOwnershipPeriods() : MemoryRepo<OwnershipPeriod>(p => p.HouseId, p => p.ValidFrom.ToString("yyyy-MM-dd")), IOwnershipPeriodRepository
{
    public Task<IReadOnlyList<OwnershipPeriod>> GetByHouseAsync(string houseId) => GetByPartitionKeyAsync(houseId);
}

public sealed class MemoryOpeningBalances() : MemoryRepo<OpeningBalance>(_ => PartitionKeys.OpeningBalance, b => b.Key), IOpeningBalanceRepository
{
    public Task<IReadOnlyList<OpeningBalance>> GetAllBalancesAsync() => GetByPartitionKeyAsync(PartitionKeys.OpeningBalance);
}

public sealed class ClosedUntil(DateOnly? day) : IClosingBoundary
{
    public DateOnly? Day { get; set; } = day;
    public Task<DateOnly?> GetLastClosedDayAsync(string? houseId = null) => Task.FromResult(Day);
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class MemoryCostEntries() : MemoryRepo<CostEntry>(e => e.ComponentId, e => e.Id), ICostEntryRepository
{
    public Task<IReadOnlyList<CostEntry>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
}

public sealed class MemoryPayments() : MemoryRepo<AdvancePayment>(p => p.HouseId, p => p.RowKey), IAdvancePaymentRepository
{
    public Task<IReadOnlyList<AdvancePayment>> GetByHouseIdAsync(string houseId) => GetByPartitionKeyAsync(houseId);

    public Task<IReadOnlyList<AdvancePayment>> GetByHouseAndPeriodAsync(string houseId, DateTime dateFrom, DateTime dateTo) =>
        Task.FromResult<IReadOnlyList<AdvancePayment>>(Items.Values.Where(p => p.HouseId == houseId && p.EffectiveDate() >= dateFrom && p.EffectiveDate() <= dateTo).ToList());
}

public sealed class MemoryInterimClosings() : MemoryRepo<InterimClosing>(_ => PartitionKeys.InterimClosing, c => c.Id), IInterimClosingRepository
{
    public Task<IReadOnlyList<InterimClosing>> GetAllClosingsAsync() => GetByPartitionKeyAsync(PartitionKeys.InterimClosing);
}

public sealed class MemoryCashBook() : MemoryRepo<CashBookEntry>(_ => PartitionKeys.CashBook, e => e.Id), ICashBookRepository
{
    public Task<IReadOnlyList<CashBookEntry>> GetAllEntriesAsync() => GetByPartitionKeyAsync(PartitionKeys.CashBook);
}
