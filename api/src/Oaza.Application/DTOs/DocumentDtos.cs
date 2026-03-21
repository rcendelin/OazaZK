namespace Oaza.Application.DTOs;

public record DocumentResponse(
    string Id,
    string Category,
    string Name,
    long FileSizeBytes,
    string ContentType,
    DateTime UploadedAt,
    string UploadedBy);

public class UploadDocumentRequest
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
