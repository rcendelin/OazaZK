using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Application.Mapping;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;
using DocumentVersionEntity = Oaza.Domain.Entities.DocumentVersion;

namespace Oaza.Functions.Endpoints;

public class DocumentFunctions
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentVersionRepository _documentVersionRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<DocumentFunctions> _logger;

    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB
    private const int MaxVersions = 10;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "image/jpeg",
        "image/png"
    };

    private static readonly string[] AllowedCategories = { "stanovy", "zapisy", "smlouvy", "ostatni" };

    private static readonly HashSet<string> AllowedCategoriesSet =
        new(AllowedCategories, StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public DocumentFunctions(
        IDocumentRepository documentRepository,
        IDocumentVersionRepository documentVersionRepository,
        IBlobStorageService blobStorageService,
        ILogger<DocumentFunctions> logger)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _documentVersionRepository = documentVersionRepository ?? throw new ArgumentNullException(nameof(documentVersionRepository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetDocuments")]
    public async Task<HttpResponseData> GetDocumentsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Unauthorized");

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var category = queryParams["category"];

            IReadOnlyList<Document> documents;
            if (!string.IsNullOrEmpty(category))
            {
                documents = await _documentRepository.GetByCategoryAsync(category);
            }
            else
            {
                // Get all documents across all categories
                var allDocuments = new List<Document>();
                foreach (var cat in AllowedCategories)
                {
                    var catDocs = await _documentRepository.GetByCategoryAsync(cat);
                    allDocuments.AddRange(catDocs);
                }
                documents = allDocuments.AsReadOnly();
            }

            var responses = documents.Select(EntityMapper.ToResponse).ToList();
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, responses);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("UploadDocument")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UploadDocumentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var name = queryParams["name"];
            var category = queryParams["category"];

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
                return await WriteErrorResponseAsync(req, 400, "Name and category are required as query parameters.");

            // Validate category
            if (!AllowedCategoriesSet.Contains(category))
                return await WriteErrorResponseAsync(req, 400, "Invalid category.");

            if (name.Length > 200)
                return await WriteErrorResponseAsync(req, 400, "Name must be at most 200 characters.");

            // Read body with size limit (20MB)
            var bodyBytes = await ReadBodyBytesWithLimitAsync(req.Body, MaxFileSizeBytes);
            if (bodyBytes.Length == 0)
                return await WriteErrorResponseAsync(req, 400, "No file content.");

            // Determine content type from Content-Type header
            var contentType = req.Headers.GetValues("Content-Type")?.FirstOrDefault() ?? "application/octet-stream";

            // Validate content type
            if (!AllowedContentTypes.Contains(contentType))
                return await WriteErrorResponseAsync(req, 400, $"Content type '{contentType}' is not allowed.");

            // Determine file extension from content type
            var extension = contentType switch
            {
                "application/pdf" => ".pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                _ => ".bin"
            };

            var documentId = Guid.NewGuid().ToString();
            var fileName = Path.GetFileName(name) + extension; // Sanitize name
            var blobPath = $"{category.ToLowerInvariant()}/{documentId}/{fileName}";

            await _blobStorageService.UploadAsync(
                BlobContainerNames.Documents, blobPath, bodyBytes, contentType);

            var document = new Document
            {
                Id = documentId,
                Category = category.ToLowerInvariant(),
                Name = name,
                BlobName = blobPath,
                FileSizeBytes = bodyBytes.Length,
                ContentType = contentType,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = user.Id,
            };

            await _documentRepository.UpsertAsync(document);

            _logger.LogInformation("Document {DocumentId} uploaded: {Name} in category {Category} ({Size} bytes).",
                documentId, name, category, bodyBytes.Length);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(document));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("DownloadDocument")]
    public async Task<HttpResponseData> DownloadDocumentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/{id}/download")] HttpRequestData req,
        string id,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Unauthorized");

            // Look up across all categories since we only have the id (rowKey)
            var document = await FindDocumentByIdAsync(id);
            if (document is null)
            {
                throw new NotFoundException("Document", id);
            }

            var stream = await _blobStorageService.DownloadAsync(BlobContainerNames.Documents, document.BlobName);
            if (stream is null)
                return await WriteErrorResponseAsync(req, 404, "File not found in storage.");

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", document.ContentType);
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{SanitizeFileName(document.Name)}\"");
            response.Body = new MemoryStream(ms.ToArray());
            return response;
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("DeleteDocument")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> DeleteDocumentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "documents/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var document = await FindDocumentByIdAsync(id);
            if (document is null)
            {
                throw new NotFoundException("Document", id);
            }

            // Delete blob from storage
            await _blobStorageService.DeleteAsync(BlobContainerNames.Documents, document.BlobName);

            // Delete entity from Table Storage (PK = category, RK = id)
            await _documentRepository.DeleteAsync(document.Category, id);

            _logger.LogInformation("Document {DocumentId} deleted.", id);

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("UploadDocumentVersion")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UploadDocumentVersionAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/{id}/versions")] HttpRequestData req,
        string id,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var document = await FindDocumentByIdAsync(id);
            if (document is null)
                throw new NotFoundException("Document", id);

            // Read body with size limit (20MB)
            var bodyBytes = await ReadBodyBytesWithLimitAsync(req.Body, MaxFileSizeBytes);
            if (bodyBytes.Length == 0)
                return await WriteErrorResponseAsync(req, 400, "No file content.");

            var contentType = req.Headers.GetValues("Content-Type")?.FirstOrDefault() ?? "application/octet-stream";
            if (!AllowedContentTypes.Contains(contentType))
                return await WriteErrorResponseAsync(req, 400, $"Content type '{contentType}' is not allowed.");

            // Get latest version number
            var latestVersion = await _documentVersionRepository.GetLatestVersionAsync(id);
            var newVersionNumber = (latestVersion?.VersionNumber ?? 0) + 1;

            // Determine file extension from content type
            var extension = contentType switch
            {
                "application/pdf" => ".pdf",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                _ => ".bin"
            };

            var fileName = Path.GetFileName(document.Name) + extension;
            var blobPath = $"{document.Category}/{id}/v{newVersionNumber}/{fileName}";

            await _blobStorageService.UploadAsync(
                BlobContainerNames.Documents, blobPath, bodyBytes, contentType);

            var version = new DocumentVersionEntity
            {
                DocumentId = id,
                VersionNumber = newVersionNumber,
                BlobName = blobPath,
                FileSizeBytes = bodyBytes.Length,
                ContentType = contentType,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = user.Id,
            };

            await _documentVersionRepository.UpsertAsync(version);

            // Update the parent document to point to the latest version
            document.BlobName = blobPath;
            document.FileSizeBytes = bodyBytes.Length;
            document.ContentType = contentType;
            document.UploadedAt = DateTime.UtcNow;
            document.UploadedBy = user.Id;
            await _documentRepository.UpsertAsync(document);

            // If versions exceed max, delete the oldest
            var allVersions = await _documentVersionRepository.GetByDocumentIdAsync(id);
            if (allVersions.Count > MaxVersions)
            {
                var toDelete = allVersions
                    .OrderBy(v => v.VersionNumber)
                    .Take(allVersions.Count - MaxVersions)
                    .ToList();

                foreach (var old in toDelete)
                {
                    await _blobStorageService.DeleteAsync(BlobContainerNames.Documents, old.BlobName);
                    await _documentVersionRepository.DeleteAsync(old.DocumentId, old.VersionNumber.ToString("D3"));
                }
            }

            _logger.LogInformation(
                "Document {DocumentId} version {Version} uploaded ({Size} bytes).",
                id, newVersionNumber, bodyBytes.Length);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(version));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("GetDocumentVersions")]
    public async Task<HttpResponseData> GetDocumentVersionsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/{id}/versions")] HttpRequestData req,
        string id,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Unauthorized");

            var document = await FindDocumentByIdAsync(id);
            if (document is null)
                throw new NotFoundException("Document", id);

            var versions = await _documentVersionRepository.GetByDocumentIdAsync(id);
            var responses = versions
                .OrderByDescending(v => v.VersionNumber)
                .Select(EntityMapper.ToResponse)
                .ToList();

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, responses);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("DownloadDocumentVersion")]
    public async Task<HttpResponseData> DownloadDocumentVersionAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/{id}/versions/{version:int}/download")] HttpRequestData req,
        string id,
        int version,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Unauthorized");

            var document = await FindDocumentByIdAsync(id);
            if (document is null)
                throw new NotFoundException("Document", id);

            var versionEntity = await _documentVersionRepository.GetAsync(id, version.ToString("D3"));
            if (versionEntity is null)
                throw new NotFoundException("DocumentVersion", $"{id}/v{version}");

            var stream = await _blobStorageService.DownloadAsync(BlobContainerNames.Documents, versionEntity.BlobName);
            if (stream is null)
                return await WriteErrorResponseAsync(req, 404, "File not found in storage.");

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", versionEntity.ContentType);
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{SanitizeFileName(document.Name)}\"");
            response.Body = new MemoryStream(ms.ToArray());
            return response;
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    /// <summary>
    /// Find a document by ID across all known categories.
    /// </summary>
    private async Task<Document?> FindDocumentByIdAsync(string id)
    {
        foreach (var category in AllowedCategories)
        {
            var doc = await _documentRepository.GetAsync(category, id);
            if (doc is not null)
            {
                return doc;
            }
        }
        return null;
    }

    private static async Task<byte[]> ReadBodyBytesWithLimitAsync(Stream body, long maxBytes)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await body.ReadAsync(buffer)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > maxBytes)
                throw new AppException($"File exceeds maximum size of {maxBytes / (1024 * 1024)} MB.", 400);
            ms.Write(buffer, 0, bytesRead);
        }
        return ms.ToArray();
    }

    private static string SanitizeFileName(string name)
    {
        var fileName = Path.GetFileName(name);
        return fileName
            .Replace("\"", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace(";", "");
    }

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) &&
            userObj is User user)
        {
            return user;
        }

        throw new AppException("User not authenticated.", 401);
    }

    private static async Task<HttpResponseData> WriteJsonResponseAsync<T>(
        HttpRequestData req, HttpStatusCode statusCode, T body)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }

    private static async Task<HttpResponseData> WriteErrorResponseAsync(HttpRequestData req, int statusCode, string message)
    {
        return await WriteJsonResponseAsync(req, (HttpStatusCode)statusCode, new { error = message });
    }
}
