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

/// <summary>
/// Cost components, allocation rules and participation (T02). Admin and Accountant read,
/// only Admin writes. Business rule violations answer 400 with every reason in <c>errors</c>.
/// </summary>
public class CostComponentFunctions
{
    private readonly CostComponentsUseCase _useCase;
    private readonly ILogger<CostComponentFunctions> _logger;


    public CostComponentFunctions(CostComponentsUseCase useCase, ILogger<CostComponentFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetCostComponents")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cost-components")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.ListAsync());

    [Function("GetCostComponent")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cost-components/{id}")] HttpRequestData req,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.GetDetailAsync(id));

    /// <summary>GET /cost-components/{id}/segments?from=yyyy-MM-dd&amp;to=yyyy-MM-dd</summary>
    [Function("GetCostComponentSegments")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetSegmentsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cost-components/{id}/segments")] HttpRequestData req,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            if (!ModelEndpoint.TryParseDay(query["from"], out var from) || !ModelEndpoint.TryParseDay(query["to"], out var to))
                throw new AppException("Zadejte období parametry from a to ve tvaru RRRR-MM-DD.");
            return await _useCase.GetSegmentsAsync(id, from, to);
        });

    [Function("CreateCostComponent")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components")] HttpRequestData req,
        FunctionContext context) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.CreateAsync(await ModelEndpoint.ReadAsync<CreateCostComponentRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    [Function("UpdateCostComponent")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> UpdateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "cost-components/{id}")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.UpdateAsync(id, await ModelEndpoint.ReadAsync<UpdateCostComponentRequest>(req), ModelEndpoint.GetActor(context)));

    [Function("AddCostComponentRule")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> AddRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components/{id}/rules")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.AddRuleAsync(id, await ModelEndpoint.ReadAsync<AddAllocationRuleRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    /// <summary>DELETE /cost-components/{id}/rules/{ruleId}?reason=</summary>
    [Function("DeleteCostComponentRule")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "cost-components/{id}/rules/{ruleId}")] HttpRequestData req,
        FunctionContext context,
        string id,
        string ruleId) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            await _useCase.DeleteRuleAsync(id, ruleId, ModelEndpoint.Query(req, "reason"), ModelEndpoint.GetActor(context));
            return (object?)null;
        }, HttpStatusCode.NoContent);

    [Function("AddCostComponentParticipation")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> AddParticipationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components/{id}/participations")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.AddParticipationAsync(id, await ModelEndpoint.ReadAsync<AddParticipationRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    [Function("EndCostComponentParticipation")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> EndParticipationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components/{id}/participations/{participationId}/end")] HttpRequestData req,
        FunctionContext context,
        string id,
        string participationId) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.EndParticipationAsync(id, participationId, await ModelEndpoint.ReadAsync<EndParticipationRequest>(req), ModelEndpoint.GetActor(context)));

    /// <summary>DELETE /cost-components/{id}/participations/{participationId}?reason=</summary>
    [Function("DeleteCostComponentParticipation")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteParticipationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "cost-components/{id}/participations/{participationId}")] HttpRequestData req,
        FunctionContext context,
        string id,
        string participationId) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            await _useCase.DeleteParticipationAsync(id, participationId, ModelEndpoint.Query(req, "reason"), ModelEndpoint.GetActor(context));
            return (object?)null;
        }, HttpStatusCode.NoContent);
}
