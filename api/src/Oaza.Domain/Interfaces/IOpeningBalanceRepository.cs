using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

/// <summary>Opening balances (T03); PK <c>OPENING</c>, RK = <see cref="OpeningBalance.Key"/>.</summary>
public interface IOpeningBalanceRepository : IRepository<OpeningBalance>
{
    Task<IReadOnlyList<OpeningBalance>> GetAllBalancesAsync();
}

/// <summary>Ownership periods (T03); PK = house id, RK = valid-from day <c>yyyy-MM-dd</c>.</summary>
public interface IOwnershipPeriodRepository : IRepository<OwnershipPeriod>
{
    Task<IReadOnlyList<OwnershipPeriod>> GetByHouseAsync(string houseId);
}
