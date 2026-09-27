using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

/// <summary>Cash book (T09); PK <c>CASH</c>, RK = entry id.</summary>
public interface ICashBookRepository : IRepository<CashBookEntry>
{
    Task<IReadOnlyList<CashBookEntry>> GetAllEntriesAsync();
}
