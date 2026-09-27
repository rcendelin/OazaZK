using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class CostEntryRepository : TableStorageRepository<CostEntry>, ICostEntryRepository
{
    public CostEntryRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.CostEntries)
    {
    }

    protected override TableEntity ToTableEntity(CostEntry entity) => TableEntityMapper.ToTableEntity(entity);

    protected override CostEntry FromTableEntity(TableEntity tableEntity) => TableEntityMapper.ToCostEntry(tableEntity);

    public Task<IReadOnlyList<CostEntry>> GetByComponentAsync(string componentId) => GetByPartitionKeyAsync(componentId);
}
