using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Deployment;
using Oaza.Application.OffBookFunds;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>
/// Off-book fund (T10) — every endpoint answers 404 while <c>OFF_BOOK_FUND_ENABLED</c> is off. Signed-in members read
/// (who paid, who not); Admin writes.
/// </summary>
public class OffBookFundFunctions
{
    private readonly OffBookFundUseCase _useCase;
    private readonly FeatureFlags _flags;
    private readonly ILogger<OffBookFundFunctions> _logger;

    public OffBookFundFunctions(OffBookFundUseCase useCase, FeatureFlags flags, ILogger<OffBookFundFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>GET /features — which optional modules are on (the frontend hides the rest).</summary>
    [Function("GetFeatures")]
    public Task<HttpResponseData> GetFeaturesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "features")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, () => Task.FromResult(new { offBookFund = _flags.OffBookFundEnabled }));

    [Function("GetOffBookFunds")]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "off-book-funds")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.ListAsync());

    [Function("GetOffBookFund")]
    public Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "off-book-funds/{id}")] HttpRequestData req,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.GetAsync(id));

    [Function("CreateOffBookFund")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "off-book-funds")] HttpRequestData req,
        FunctionContext context) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.CreateAsync(await ModelEndpoint.ReadAsync<SaveOffBookFundRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    [Function("UpdateOffBookFund")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> UpdateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "off-book-funds/{id}")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.UpdateAsync(id, await ModelEndpoint.ReadAsync<SaveOffBookFundRequest>(req), ModelEndpoint.GetActor(context)));

    [Function("AddOffBookFundRecord")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> AddRecordAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "off-book-funds/{id}/records")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.AddRecordAsync(id, await ModelEndpoint.ReadAsync<AddFundRecordRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    /// <summary>DELETE /off-book-funds/{id}/records/{recordId}?reason=</summary>
    [Function("DeleteOffBookFundRecord")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteRecordAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "off-book-funds/{id}/records/{recordId}")] HttpRequestData req,
        FunctionContext context,
        string id,
        string recordId) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            await _useCase.DeleteRecordAsync(id, recordId, ModelEndpoint.Query(req, "reason"), ModelEndpoint.GetActor(context));
            return (object?)null;
        }, HttpStatusCode.NoContent);
}
