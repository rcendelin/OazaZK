using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Oaza.Application.Interfaces;

namespace Oaza.Infrastructure.Storage;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(BlobServiceClient blobServiceClient, ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> UploadAsync(string containerName, string blobPath, byte[] content, string contentType)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var blobClient = containerClient.GetBlobClient(blobPath);

        using var stream = new MemoryStream(content);
        await blobClient.UploadAsync(stream, overwrite: true);
        await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = contentType });

        _logger.LogInformation(
            "Uploaded blob {BlobPath} to container {Container} ({Size} bytes).",
            blobPath, containerName, content.Length);

        return blobClient.Uri.ToString();
    }

    public async Task<Stream?> DownloadAsync(string containerName, string blobPath)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        if (!await blobClient.ExistsAsync())
        {
            _logger.LogWarning("Blob {BlobPath} not found in container {Container}.", blobPath, containerName);
            return null;
        }

        var download = await blobClient.DownloadContentAsync();
        return new MemoryStream(download.Value.Content.ToArray());
    }

    public async Task<string> GetDownloadUrlAsync(string containerName, string blobPath, TimeSpan expiry)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        if (!await blobClient.ExistsAsync())
        {
            throw new InvalidOperationException(
                $"Blob '{blobPath}' does not exist in container '{containerName}'.");
        }

        // Generate SAS URL with read permission
        if (blobClient.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName = blobPath,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiry),
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return sasUri.ToString();
        }

        // Fallback: use user delegation SAS if account key is not available
        var userDelegationKey = await _blobServiceClient.GetUserDelegationKeyAsync(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(expiry));

        var delegationSasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobPath,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiry),
        };
        delegationSasBuilder.SetPermissions(BlobSasPermissions.Read);

        var blobUriBuilder = new BlobUriBuilder(blobClient.Uri)
        {
            Sas = delegationSasBuilder.ToSasQueryParameters(userDelegationKey, _blobServiceClient.AccountName),
        };

        return blobUriBuilder.ToUri().ToString();
    }

    public async Task<bool> ExistsAsync(string containerName, string blobPath)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        return await blobClient.ExistsAsync();
    }
}
