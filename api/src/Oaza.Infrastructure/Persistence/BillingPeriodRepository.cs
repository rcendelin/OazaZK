using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class BillingPeriodRepository : TableStorageRepository<BillingPeriod>, IBillingPeriodRepository
{
    public BillingPeriodRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.BillingPeriods)
    {
    }

    protected override TableEntity ToTableEntity(BillingPeriod entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override BillingPeriod FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToBillingPeriod(tableEntity);
}
