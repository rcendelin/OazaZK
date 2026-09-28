using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

/// <summary>Cost entries (T06); PK = component id, RK = entry id.</summary>
public interface ICostEntryRepository : IRepository<CostEntry>
{
    Task<IReadOnlyList<CostEntry>> GetByComponentAsync(string componentId);
}
