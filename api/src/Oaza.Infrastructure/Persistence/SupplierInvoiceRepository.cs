using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class SupplierInvoiceRepository : TableStorageRepository<SupplierInvoice>, ISupplierInvoiceRepository
{
    public SupplierInvoiceRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.SupplierInvoices)
    {
    }

    protected override TableEntity ToTableEntity(SupplierInvoice entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override SupplierInvoice FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToSupplierInvoice(tableEntity);

    public async Task<IReadOnlyList<SupplierInvoice>> GetByYearAsync(int year)
    {
        var all = await GetByPartitionKeyAsync(PartitionKeys.Invoice);
        return all.Where(i => i.Year == year)
            .ToList()
            .AsReadOnly();
    }

    public async Task<IReadOnlyList<SupplierInvoice>> GetByPeriodAsync(DateTime dateFrom, DateTime dateTo)
    {
        var all = await GetByPartitionKeyAsync(PartitionKeys.Invoice);
        return all.Where(i =>
        {
            // Invoice month falls within the period date range
            var invoiceDate = new DateTime(i.Year, i.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            return invoiceDate >= dateFrom && invoiceDate <= dateTo;
        })
        .ToList()
        .AsReadOnly();
    }
}
