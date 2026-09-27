using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>Cost entries of components (T06). Admin and Accountant read, only Admin writes.</summary>
public class CostEntryFunctions
{
    private readonly CostEntriesUseCase _useCase;
    private readonly ILogger<CostEntryFunctions> _logger;

    public CostEntryFunctions(CostEntriesUseCase useCase, ILogger<CostEntryFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>GET /cost-components/{id}/entries?from=&amp;to= — entries whose period overlaps the range.</summary>
    [Function("GetCostEntries")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cost-components/{id}/entries")] HttpRequestData req,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            DateOnly? from = null, to = null;
            var fromText = ModelEndpoint.Query(req, "from");
            var toText = ModelEndpoint.Query(req, "to");
            if (!string.IsNullOrEmpty(fromText))
                from = ModelEndpoint.TryParseDay(fromText, out var f) ? f : throw new AppException("Datum od zadejte ve tvaru RRRR-MM-DD.");
            if (!string.IsNullOrEmpty(toText))
                to = ModelEndpoint.TryParseDay(toText, out var t) ? t : throw new AppException("Datum do zadejte ve tvaru RRRR-MM-DD.");
            return await _useCase.ListAsync(id, from, to);
        });

    [Function("GetCostEntryAllocation")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetAllocationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cost-components/{id}/entries/{entryId}/allocation")] HttpRequestData req,
        string id,
        string entryId) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.GetAllocationAsync(id, entryId));

    [Function("CreateCostEntry")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components/{id}/entries")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.CreateAsync(id, await ModelEndpoint.ReadAsync<SaveCostEntryRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    [Function("CreateRecurringAdvances")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> CreateRecurringAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components/{id}/entries/recurring")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.CreateRecurringAsync(id, await ModelEndpoint.ReadAsync<RecurringAdvanceRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    [Function("UpdateCostEntry")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> UpdateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "cost-components/{id}/entries/{entryId}")] HttpRequestData req,
        FunctionContext context,
        string id,
        string entryId) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.UpdateAsync(id, entryId, await ModelEndpoint.ReadAsync<SaveCostEntryRequest>(req), ModelEndpoint.GetActor(context)));

    /// <summary>DELETE /cost-components/{id}/entries/{entryId}?reason=</summary>
    [Function("DeleteCostEntry")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "cost-components/{id}/entries/{entryId}")] HttpRequestData req,
        FunctionContext context,
        string id,
        string entryId) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            await _useCase.DeleteAsync(id, entryId, ModelEndpoint.Query(req, "reason"), ModelEndpoint.GetActor(context));
            return (object?)null;
        }, HttpStatusCode.NoContent);
}
