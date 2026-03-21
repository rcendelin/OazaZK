using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

public class DocumentRepository : TableStorageRepository<Document>, IDocumentRepository
{
    public DocumentRepository(TableServiceClient serviceClient)
        : base(serviceClient, TableNames.Documents)
    {
    }

    protected override TableEntity ToTableEntity(Document entity) =>
        TableEntityMapper.ToTableEntity(entity);

    protected override Document FromTableEntity(TableEntity tableEntity) =>
        TableEntityMapper.ToDocument(tableEntity);

    public async Task<IReadOnlyList<Document>> GetByCategoryAsync(string category)
    {
        // PK = category
        return await GetByPartitionKeyAsync(category);
    }
}
