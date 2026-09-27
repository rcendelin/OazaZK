using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class CashBookRepository : TableStorageRepository<CashBookEntry>, ICashBookRepository
{
    public CashBookRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.CashBook)
    {
    }

    protected override TableEntity ToTableEntity(CashBookEntry entity) => TableEntityMapper.ToTableEntity(entity);

    protected override CashBookEntry FromTableEntity(TableEntity tableEntity) => TableEntityMapper.ToCashBookEntry(tableEntity);

    public Task<IReadOnlyList<CashBookEntry>> GetAllEntriesAsync() => GetByPartitionKeyAsync(PartitionKeys.CashBook);
}
