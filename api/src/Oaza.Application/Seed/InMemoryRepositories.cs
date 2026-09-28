using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Seed;

// In-memory implementations of the repositories: the seed import (T13) runs its dry run over a copy of the data
// (nothing is written until it passes); tests use them as stateful fakes.

/// <summary>In-memory table: (PK, RK) → entity.</summary>
public class MemoryRepo<T>(Func<T, string> pk, Func<T, string> rk) : IRepository<T> where T : class
{
    public readonly Dictionary<(string, string), T> Items = new();

    /// <summary>
    /// Hand out and store copies, like a real table: a use case that changes an entity and then fails validation
    /// must not leave the change behind (the seed dry run). Off by default — tests change stored entities in place.
    /// </summary>
    public bool CopyOnAccess { get; init; }

    public Task<T?> GetAsync(string partitionKey, string rowKey) => Task.FromResult(Out(Items.GetValueOrDefault((partitionKey, rowKey))));

    public Task<IReadOnlyList<T>> GetByPartitionKeyAsync(string partitionKey) =>
        Task.FromResult<IReadOnlyList<T>>(Items.Where(i => i.Key.Item1 == partitionKey).Select(i => Out(i.Value)!).ToList());

    public Task<IReadOnlyList<T>> GetAllAsync() => Task.FromResult<IReadOnlyList<T>>(Items.Values.Select(v => Out(v)!).ToList());

    public Task UpsertAsync(T entity)
    {
        Items[(pk(entity), rk(entity))] = Out(entity)!;
        return Task.CompletedTask;
    }

    private T? Out(T? entity) =>
        CopyOnAccess && entity is not null
            ? System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(entity))
            : entity;

    public Task DeleteAsync(string partitionKey, string rowKey)
    {
        Items.Remove((partitionKey, rowKey));
        return Task.CompletedTask;
    }

    /// <summary>Copies every entity of <paramref name="source"/> into this table.</summary>
    public async Task LoadFromAsync(IRepository<T> source)
    {
        foreach (var entity in await source.GetAllAsync())
            await UpsertAsync(entity);
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
    public async Task<IReadOnlyList<WaterMeter>> GetByHouseIdAsync(string houseId) =>
        (await GetAllAsync()).Where(m => m.HouseId == houseId).ToList();
}

public sealed class MemoryReadings() : MemoryRepo<MeterReading>(r => r.MeterId, r => InvertedTimestamp.FromDateTime(r.ReadingDate)), IMeterReadingRepository
{
    public Task<IReadOnlyList<MeterReading>> GetByMeterIdAsync(string meterId) => GetByPartitionKeyAsync(meterId);

    public async Task<IReadOnlyList<MeterReading>> GetLatestByMeterIdAsync(string meterId, int count) =>
        (await GetByPartitionKeyAsync(meterId)).OrderByDescending(r => r.ReadingDate).Take(count).ToList();
}

public sealed class MemoryOwnershipPeriods() : MemoryRepo<OwnershipPeriod>(p => p.HouseId, p => p.ValidFrom.ToString("yyyy-MM-dd")), IOwnershipPeriodRepository
{
    public Task<IReadOnlyList<OwnershipPeriod>> GetByHouseAsync(string houseId) => GetByPartitionKeyAsync(houseId);
}

public sealed class MemoryOpeningBalances() : MemoryRepo<OpeningBalance>(_ => PartitionKeys.OpeningBalance, b => b.Key), IOpeningBalanceRepository
{
    public Task<IReadOnlyList<OpeningBalance>> GetAllBalancesAsync() => GetByPartitionKeyAsync(PartitionKeys.OpeningBalance);
}

public sealed class MemoryCostEntries() : MemoryRepo<CostEntry>(e => e.ComponentId, e => e.Id), ICostEntryRepository
{
    public Task<IReadOnlyList<CostEntry>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
}

public sealed class MemoryPayments() : MemoryRepo<AdvancePayment>(p => p.HouseId, p => p.RowKey), IAdvancePaymentRepository
{
    public Task<IReadOnlyList<AdvancePayment>> GetByHouseIdAsync(string houseId) => GetByPartitionKeyAsync(houseId);

    public async Task<IReadOnlyList<AdvancePayment>> GetByHouseAndPeriodAsync(string houseId, DateTime dateFrom, DateTime dateTo) =>
        (await GetByPartitionKeyAsync(houseId)).Where(p => p.EffectiveDate() >= dateFrom && p.EffectiveDate() <= dateTo).ToList();
}
