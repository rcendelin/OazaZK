using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface ISupplierInvoiceRepository : IRepository<SupplierInvoice>
{
    Task<IReadOnlyList<SupplierInvoice>> GetByYearAsync(int year);
    Task<IReadOnlyList<SupplierInvoice>> GetByPeriodAsync(DateTime dateFrom, DateTime dateTo);
}
