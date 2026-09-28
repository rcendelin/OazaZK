using FluentAssertions;
using Oaza.Application.Documents;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.Documents;

public class DocumentVersioningTests
{
    private sealed class Versions() : MemoryRepo<DocumentVersion>(v => v.DocumentId, v => v.VersionNumber.ToString("D3")), IDocumentVersionRepository
    {
        public Task<IReadOnlyList<DocumentVersion>> GetByDocumentIdAsync(string documentId) => GetByPartitionKeyAsync(documentId);

        public async Task<DocumentVersion?> GetLatestVersionAsync(string documentId) =>
            (await GetByPartitionKeyAsync(documentId)).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
    }

    private static readonly Document Doc = new()
    {
        Id = "d1", Name = "Zápis.pdf", BlobName = "zapisy/d1/Zápis.pdf", FileSizeBytes = 584, ContentType = "application/pdf",
        UploadedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), UploadedBy = "u1",
    };

    [Fact]
    public async Task FirstNewVersion_KeepsTheOriginalAsVersionOne()
    {
        var versions = new Versions();

        var latest = await DocumentVersioning.LatestKeepingOriginalAsync(versions, Doc);

        latest.VersionNumber.Should().Be(1);
        versions.Items.Values.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            DocumentId = "d1", VersionNumber = 1, BlobName = "zapisy/d1/Zápis.pdf", FileSizeBytes = 584L, ContentType = "application/pdf", UploadedBy = "u1",
        });
    }

    [Fact]
    public async Task ExistingHistory_IsLeftAlone()
    {
        var versions = new Versions();
        await versions.UpsertAsync(new DocumentVersion { DocumentId = "d1", VersionNumber = 1, BlobName = "a" });
        await versions.UpsertAsync(new DocumentVersion { DocumentId = "d1", VersionNumber = 2, BlobName = "b" });

        var latest = await DocumentVersioning.LatestKeepingOriginalAsync(versions, Doc);

        latest.VersionNumber.Should().Be(2);
        versions.Items.Should().HaveCount(2);
    }
}
