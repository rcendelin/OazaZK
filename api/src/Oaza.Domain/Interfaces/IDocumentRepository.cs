using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IDocumentRepository : IRepository<Document>
{
    Task<IReadOnlyList<Document>> GetByCategoryAsync(string category);
}
