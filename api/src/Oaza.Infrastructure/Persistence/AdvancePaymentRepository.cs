using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
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
            // Only component-split money-in counts toward a period's settlement.
            // Payouts and opening balances are net-level and must never inflate a
            // period's advances (they would distort the water true-up).
            if (p.Type is not (PaymentType.Advance or PaymentType.Doplatek)) return false;
            var effectiveDate = p.EffectiveDate();
            return effectiveDate >= dateFrom && effectiveDate <= dateTo;
        })
        .ToList()
        .AsReadOnly();
    }
}
