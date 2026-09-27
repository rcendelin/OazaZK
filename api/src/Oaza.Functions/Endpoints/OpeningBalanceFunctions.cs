using System.Globalization;
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
/// Opening balances and ownership periods (T03). Admin and Accountant read, only Admin writes.
/// Business rule violations answer 400 with every reason in <c>errors</c>.
/// </summary>
public class OpeningBalanceFunctions
{
    private readonly OpeningBalancesUseCase _useCase;
    private readonly ILogger<OpeningBalanceFunctions> _logger;


    public OpeningBalanceFunctions(OpeningBalancesUseCase useCase, ILogger<OpeningBalanceFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetOwnershipPeriods")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetOwnershipPeriodsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ownership-periods")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.GetOwnershipPeriodsAsync());

    [Function("StartOwnership")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> StartOwnershipAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ownership-periods/start")] HttpRequestData req,
        FunctionContext context) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.StartOwnershipAsync(await ModelEndpoint.ReadAsync<StartOwnershipRequest>(req), ModelEndpoint.GetActor(context)));

    [Function("GetOpeningBalances")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "opening-balances")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.ListAsync());

    /// <summary>GET /opening-balances/component-credit-preview?componentId=&amp;date=yyyy-MM-dd&amp;value=-20000</summary>
    [Function("PreviewComponentCredit")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> PreviewComponentCreditAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "opening-balances/component-credit-preview")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            if (string.IsNullOrWhiteSpace(query["componentId"])
                || !DateOnly.TryParseExact(query["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(query["value"], NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                throw new AppException("Zadejte componentId, date (RRRR-MM-DD) a value.");
            }
            return await _useCase.PreviewComponentCreditAsync(query["componentId"]!, date, value);
        });

    [Function("CreateOpeningBalance")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "opening-balances")] HttpRequestData req,
        FunctionContext context) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.CreateAsync(await ModelEndpoint.ReadAsync<SaveOpeningBalanceRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    /// <summary>PUT /opening-balances/{key} — key URL-encoded (it contains „|“).</summary>
    [Function("UpdateOpeningBalance")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> UpdateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "opening-balances/{key}")] HttpRequestData req,
        FunctionContext context,
        string key) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.UpdateAsync(Uri.UnescapeDataString(key), await ModelEndpoint.ReadAsync<SaveOpeningBalanceRequest>(req), ModelEndpoint.GetActor(context)));

    /// <summary>DELETE /opening-balances/{key}?reason=</summary>
    [Function("DeleteOpeningBalance")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "opening-balances/{key}")] HttpRequestData req,
        FunctionContext context,
        string key) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            var reason = ModelEndpoint.Query(req, "reason");
            await _useCase.DeleteAsync(Uri.UnescapeDataString(key), reason, ModelEndpoint.GetActor(context));
            return (object?)null;
        }, HttpStatusCode.NoContent);
}
