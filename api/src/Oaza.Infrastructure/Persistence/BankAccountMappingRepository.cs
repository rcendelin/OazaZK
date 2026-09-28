using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class BankAccountMappingRepository : TableStorageRepository<BankAccountMapping>, IBankAccountMappingRepository
{
    public BankAccountMappingRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.BankAccountMappings)
    {
    }

    protected override TableEntity ToTableEntity(BankAccountMapping entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override BankAccountMapping FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToBankAccountMapping(tableEntity);

    public Task<IReadOnlyList<BankAccountMapping>> GetAllMappingsAsync() =>
        GetByPartitionKeyAsync(PartitionKeys.BankAccountMapping);
}
