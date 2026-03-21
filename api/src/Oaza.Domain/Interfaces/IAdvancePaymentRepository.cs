using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IAdvancePaymentRepository : IRepository<AdvancePayment>
{
    Task<IReadOnlyList<AdvancePayment>> GetByHouseIdAsync(string houseId);
    Task<IReadOnlyList<AdvancePayment>> GetByHouseAndPeriodAsync(string houseId, DateTime dateFrom, DateTime dateTo);
}
