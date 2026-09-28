using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Documents;

/// <summary>Version history of a document (live E2E finding: the original file dropped out of the history).</summary>
public static class DocumentVersioning
{
    /// <summary>
    /// The latest version, creating version 1 from the document's current file when it has none yet — the first
    /// upload has no version row, and without it the original would be lost when a new version replaces it.
    /// </summary>
    public static async Task<DocumentVersion> LatestKeepingOriginalAsync(IDocumentVersionRepository versions, Document document)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(document);
        var latest = await versions.GetLatestVersionAsync(document.Id);
        if (latest is not null)
            return latest;

        var original = new DocumentVersion
        {
            DocumentId = document.Id,
            VersionNumber = 1,
            BlobName = document.BlobName,
            FileSizeBytes = document.FileSizeBytes,
            ContentType = document.ContentType,
            UploadedAt = document.UploadedAt,
            UploadedBy = document.UploadedBy,
        };
        await versions.UpsertAsync(original);
        return original;
    }
}
