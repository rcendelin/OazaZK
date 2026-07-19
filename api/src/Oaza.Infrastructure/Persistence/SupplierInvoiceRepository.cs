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
        // Filter by the invoice's issue-date year (when it was received), not the
        // header Year field (= earliest line-item's year). A multi-year invoice
        // whose oldest line predates the requested year must still be findable
        // under the year it was actually issued. Settlement is unaffected — it
        // reads all invoices via GetByPartitionKeyAsync and matches per line item.
        var all = await GetByPartitionKeyAsync(PartitionKeys.Invoice);
        return all.Where(i => i.IssuedDate.Year == year)
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
