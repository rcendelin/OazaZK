using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class SettlementRepository : TableStorageRepository<Settlement>, ISettlementRepository
{
    public SettlementRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.Settlements)
    {
    }

    protected override TableEntity ToTableEntity(Settlement entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override Settlement FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToSettlement(tableEntity);

    public async Task<IReadOnlyList<Settlement>> GetByPeriodIdAsync(string periodId)
    {
        // PK = periodId
        return await GetByPartitionKeyAsync(periodId);
    }
}
