using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class MeterReadingRepository : TableStorageRepository<MeterReading>, IMeterReadingRepository
{
    public MeterReadingRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.MeterReadings)
    {
    }

    protected override TableEntity ToTableEntity(MeterReading entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override MeterReading FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToMeterReading(tableEntity);

    public async Task<IReadOnlyList<MeterReading>> GetByMeterIdAsync(string meterId)
    {
        // PK = meterId, RK = inverted timestamp → results already sorted newest first
        return await GetByPartitionKeyAsync(meterId);
    }

    public async Task<IReadOnlyList<MeterReading>> GetLatestByMeterIdAsync(string meterId, int count)
    {
        var client = await GetTableClientAsync();
        var results = new List<MeterReading>();

        // Query by partition key; inverted timestamp RowKey ensures newest first
        await foreach (var entity in client.QueryAsync<TableEntity>(
            filter: TableClient.CreateQueryFilter($"PartitionKey eq {meterId}")))
        {
            results.Add(FromTableEntity(entity));
            if (results.Count >= count)
            {
                break;
            }
        }

        return results.AsReadOnly();
    }
}
