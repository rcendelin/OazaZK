using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class FinancialRecordRepository : TableStorageRepository<FinancialRecord>, IFinancialRecordRepository
{
    public FinancialRecordRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.FinancialRecords)
    {
    }

    protected override TableEntity ToTableEntity(FinancialRecord entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override FinancialRecord FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToFinancialRecord(tableEntity);

    public async Task<IReadOnlyList<FinancialRecord>> GetByYearAsync(int year)
    {
        // PK = year as string
        return await GetByPartitionKeyAsync(year.ToString());
    }

    public async Task<IReadOnlyList<FinancialRecord>> GetByYearAndCategoryAsync(int year, string category)
    {
        var all = await GetByPartitionKeyAsync(year.ToString());
        return all.Where(r =>
            string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();
    }
}
