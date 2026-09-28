using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Application.UseCases;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>Interim closings (T08). Admin and Accountant read, only Admin closes or removes the latest closing.</summary>
public class InterimClosingFunctions
{
    private readonly InterimClosingsUseCase _useCase;
    private readonly ILogger<InterimClosingFunctions> _logger;

    public InterimClosingFunctions(InterimClosingsUseCase useCase, ILogger<InterimClosingFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetInterimClosings")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "interim-closings")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.ListAsync());

    /// <summary>GET /interim-closings/{id} — id URL-encoded (it contains „|“).</summary>
    [Function("GetInterimClosing")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "interim-closings/{id}")] HttpRequestData req,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.GetAsync(Uri.UnescapeDataString(id)));

    /// <summary>GET /interim-closings/{id}/export?format=xlsx|csv — for the accountant (annual closing).</summary>
    [Function("ExportInterimClosing")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ExportAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "interim-closings/{id}/export")] HttpRequestData req,
        string id) =>
        ModelEndpoint.HandleFileAsync(req, _logger, async () =>
        {
            var file = await _useCase.ExportAsync(Uri.UnescapeDataString(id), ModelEndpoint.Query(req, "format") ?? "xlsx");
            return (file.Content, file.ContentType, file.FileName);
        });

    [Function("CreateInterimClosing")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "interim-closings")] HttpRequestData req,
        FunctionContext context) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.CreateAsync(await ModelEndpoint.ReadAsync<CreateInterimClosingRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    /// <summary>DELETE /interim-closings/{id}?reason= — only the latest closing.</summary>
    [Function("DeleteInterimClosing")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "interim-closings/{id}")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            await _useCase.DeleteAsync(Uri.UnescapeDataString(id), ModelEndpoint.Query(req, "reason"), ModelEndpoint.GetActor(context));
            return (object?)null;
        }, HttpStatusCode.NoContent);
}
