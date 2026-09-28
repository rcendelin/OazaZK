using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IBankTransactionRepository : IRepository<BankTransaction>
{
    /// <summary>All processed movements of one own account.</summary>
    Task<IReadOnlyList<BankTransaction>> GetByAccountAsync(string ownAccountKey);
}
