namespace Oaza.Domain.Entities;

public class Document
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>Invoices and settlements (T11): the cost component the document belongs to, if known.</summary>
    public string? ComponentId { get; set; }
}
