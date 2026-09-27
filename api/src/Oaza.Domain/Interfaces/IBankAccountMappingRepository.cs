using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IBankAccountMappingRepository : IRepository<BankAccountMapping>
{
    /// <summary>All mappings (single partition).</summary>
    Task<IReadOnlyList<BankAccountMapping>> GetAllMappingsAsync();
}
