using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IDocumentVersionRepository : IRepository<DocumentVersion>
{
    Task<IReadOnlyList<DocumentVersion>> GetByDocumentIdAsync(string documentId);
    Task<DocumentVersion?> GetLatestVersionAsync(string documentId);
}
