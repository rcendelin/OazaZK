using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class WaterMeterRepository : TableStorageRepository<WaterMeter>, IWaterMeterRepository
{
    public WaterMeterRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.WaterMeters)
    {
    }

    protected override TableEntity ToTableEntity(WaterMeter entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override WaterMeter FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToWaterMeter(tableEntity);

    public async Task<IReadOnlyList<WaterMeter>> GetByHouseIdAsync(string houseId)
    {
        var meters = await GetByPartitionKeyAsync(PartitionKeys.Meter);
        return meters.Where(m =>
            string.Equals(m.HouseId, houseId, StringComparison.Ordinal))
            .ToList()
            .AsReadOnly();
    }
}
