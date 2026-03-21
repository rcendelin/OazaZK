using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class DocumentVersionRepository : TableStorageRepository<DocumentVersion>, IDocumentVersionRepository
{
    public DocumentVersionRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.DocumentVersions)
    {
    }

    protected override TableEntity ToTableEntity(DocumentVersion entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override DocumentVersion FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToDocumentVersion(tableEntity);

    public async Task<IReadOnlyList<DocumentVersion>> GetByDocumentIdAsync(string documentId)
    {
        // PK = documentId
        return await GetByPartitionKeyAsync(documentId);
    }

    public async Task<DocumentVersion?> GetLatestVersionAsync(string documentId)
    {
        var versions = await GetByDocumentIdAsync(documentId);
        return versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
    }
}
