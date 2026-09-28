using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class InterimClosingRepository : TableStorageRepository<InterimClosing>, IInterimClosingRepository
{
    public InterimClosingRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.InterimClosings)
    {
    }

    protected override TableEntity ToTableEntity(InterimClosing entity) => TableEntityMapper.ToTableEntity(entity);

    protected override InterimClosing FromTableEntity(TableEntity tableEntity) => TableEntityMapper.ToInterimClosing(tableEntity);

    public Task<IReadOnlyList<InterimClosing>> GetAllClosingsAsync() => GetByPartitionKeyAsync(PartitionKeys.InterimClosing);
}
