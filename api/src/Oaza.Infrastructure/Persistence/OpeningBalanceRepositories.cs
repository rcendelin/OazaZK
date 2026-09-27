using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class OwnershipPeriodRepository : TableStorageRepository<OwnershipPeriod>, IOwnershipPeriodRepository
{
    public OwnershipPeriodRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.OwnershipPeriods)
    {
    }

    protected override TableEntity ToTableEntity(OwnershipPeriod entity) => TableEntityMapper.ToTableEntity(entity);

    protected override OwnershipPeriod FromTableEntity(TableEntity tableEntity) => TableEntityMapper.ToOwnershipPeriod(tableEntity);

    public Task<IReadOnlyList<OwnershipPeriod>> GetByHouseAsync(string houseId) => GetByPartitionKeyAsync(houseId);
}

public class OpeningBalanceRepository : TableStorageRepository<OpeningBalance>, IOpeningBalanceRepository
{
    public OpeningBalanceRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.OpeningBalances)
    {
    }

    protected override TableEntity ToTableEntity(OpeningBalance entity) => TableEntityMapper.ToTableEntity(entity);

    protected override OpeningBalance FromTableEntity(TableEntity tableEntity) => TableEntityMapper.ToOpeningBalance(tableEntity);

    public Task<IReadOnlyList<OpeningBalance>> GetAllBalancesAsync() => GetByPartitionKeyAsync(PartitionKeys.OpeningBalance);
}
