using Azure;
using Azure.Data.Tables;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

/// <summary>
/// Generic base implementation of IRepository using Azure Table Storage.
/// Subclasses provide entity-specific mapping via abstract methods.
/// </summary>
public abstract class TableStorageRepository<T> : IRepository<T> where T : class
{
    private readonly TableServiceClient _serviceClient;
    private readonly string _tableName;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private TableClient? _tableClient;

    protected TableStorageRepository(TableServiceClient serviceClient, string tableName)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
    }

    protected async Task<TableClient> GetTableClientAsync()
    {
        if (_tableClient is not null)
            return _tableClient;

        await _initLock.WaitAsync();
        try
        {
            if (_tableClient is not null)
                return _tableClient;

            var client = _serviceClient.GetTableClient(_tableName);
            await client.CreateIfNotExistsAsync();
            _tableClient = client;
            return client;
        }
        finally
        {
            _initLock.Release();
        }
    }

    protected abstract TableEntity ToTableEntity(T entity);
    protected abstract T FromTableEntity(TableEntity tableEntity);

    public async Task<T?> GetAsync(string partitionKey, string rowKey)
    {
        var client = await GetTableClientAsync();
        try
        {
            var response = await client.GetEntityAsync<TableEntity>(partitionKey, rowKey);
            return FromTableEntity(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<T>> GetByPartitionKeyAsync(string partitionKey)
    {
        var client = await GetTableClientAsync();
        var results = new List<T>();

        await foreach (var entity in client.QueryAsync<TableEntity>(
            filter: TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}")))
        {
            results.Add(FromTableEntity(entity));
        }

        return results.AsReadOnly();
    }

    public async Task<IReadOnlyList<T>> GetAllAsync()
    {
        var client = await GetTableClientAsync();
        var results = new List<T>();

        await foreach (var entity in client.QueryAsync<TableEntity>())
        {
            results.Add(FromTableEntity(entity));
        }

        return results.AsReadOnly();
    }

    public async Task UpsertAsync(T entity)
    {
        var client = await GetTableClientAsync();
        var tableEntity = ToTableEntity(entity);
        await client.UpsertEntityAsync(tableEntity, TableUpdateMode.Replace);
    }

    public async Task DeleteAsync(string partitionKey, string rowKey)
    {
        var client = await GetTableClientAsync();
        try
        {
            await client.DeleteEntityAsync(partitionKey, rowKey);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Entity already deleted — idempotent
        }
    }
}
