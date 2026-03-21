using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IWaterMeterRepository : IRepository<WaterMeter>
{
    Task<IReadOnlyList<WaterMeter>> GetByHouseIdAsync(string houseId);
}
