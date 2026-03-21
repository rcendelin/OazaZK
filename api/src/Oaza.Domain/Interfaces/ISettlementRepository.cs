using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface ISettlementRepository : IRepository<Settlement>
{
    Task<IReadOnlyList<Settlement>> GetByPeriodIdAsync(string periodId);
}
