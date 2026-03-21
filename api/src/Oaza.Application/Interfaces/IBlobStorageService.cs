namespace Oaza.Application.Interfaces;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string containerName, string blobPath, byte[] content, string contentType);
    Task<Stream?> DownloadAsync(string containerName, string blobPath);
    Task<string> GetDownloadUrlAsync(string containerName, string blobPath, TimeSpan expiry);
    Task<bool> ExistsAsync(string containerName, string blobPath);
}
