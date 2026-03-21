using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IMeterReadingRepository : IRepository<MeterReading>
{
    Task<IReadOnlyList<MeterReading>> GetByMeterIdAsync(string meterId);
    Task<IReadOnlyList<MeterReading>> GetLatestByMeterIdAsync(string meterId, int count);
}
