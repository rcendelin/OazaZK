namespace Oaza.Domain.Entities;

public class DocumentVersion
{
    public string DocumentId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string BlobName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
}
