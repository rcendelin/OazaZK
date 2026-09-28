using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

/// <summary>Interim closings (T08); PK <c>CLOSING</c>, RK = <see cref="InterimClosing.Id"/>.</summary>
public interface IInterimClosingRepository : IRepository<InterimClosing>
{
    Task<IReadOnlyList<InterimClosing>> GetAllClosingsAsync();
}
