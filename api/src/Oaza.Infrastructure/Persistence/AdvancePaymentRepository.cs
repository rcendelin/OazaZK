using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class AdvancePaymentRepository : TableStorageRepository<AdvancePayment>, IAdvancePaymentRepository
{
    public AdvancePaymentRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.AdvancePayments)
    {
    }

    protected override TableEntity ToTableEntity(AdvancePayment entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override AdvancePayment FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToAdvancePayment(tableEntity);

    public async Task<IReadOnlyList<AdvancePayment>> GetByHouseIdAsync(string houseId)
    {
        // PK = houseId
        return await GetByPartitionKeyAsync(houseId);
    }

    public async Task<IReadOnlyList<AdvancePayment>> GetByHouseAndPeriodAsync(
        string houseId, DateTime dateFrom, DateTime dateTo)
    {
        var all = await GetByPartitionKeyAsync(houseId);
        return all.Where(p =>
        {
            var paymentDate = new DateTime(p.Year, p.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return paymentDate >= dateFrom && paymentDate <= dateTo;
        })
        .ToList()
        .AsReadOnly();
    }
}
