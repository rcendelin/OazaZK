using Azure;
using Azure.Data.Tables;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

/// <summary>
/// Persists the singleton <see cref="AdvanceSettings"/> record
/// (table "AdvanceSettings", PK="SETTINGS", RK="advances").
/// </summary>
public class AdvanceSettingsRepository : IAdvanceSettingsRepository
{
    private const string TableName = "AdvanceSettings";
    private const string PartitionKey = "SETTINGS";
    private const string RowKey = "advances";

    private readonly TableServiceClient _serviceClient;

    public AdvanceSettingsRepository(TableServiceClient serviceClient)
    {
        _serviceClient = serviceClient;
    }

    public async Task<AdvanceSettings> GetAsync()
    {
        var tableClient = _serviceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();
        try
        {
            var response = await tableClient.GetEntityAsync<TableEntity>(PartitionKey, RowKey);
            return TableEntityMapper.ToAdvanceSettings(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new AdvanceSettings();
        }
    }

    public async Task UpsertAsync(AdvanceSettings settings)
    {
        var tableClient = _serviceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();
        await tableClient.UpsertEntityAsync(TableEntityMapper.ToTableEntity(settings));
    }
}
