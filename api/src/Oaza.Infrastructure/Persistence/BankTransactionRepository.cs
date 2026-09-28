using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class BankTransactionRepository : TableStorageRepository<BankTransaction>, IBankTransactionRepository
{
    public BankTransactionRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.BankTransactions)
    {
    }

    protected override TableEntity ToTableEntity(BankTransaction entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override BankTransaction FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToBankTransaction(tableEntity);

    public Task<IReadOnlyList<BankTransaction>> GetByAccountAsync(string ownAccountKey) =>
        GetByPartitionKeyAsync(ownAccountKey);
}
