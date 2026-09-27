using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Audit;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Entities;
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public OpeningBalanceFunctions(OpeningBalancesUseCase useCase, ILogger<OpeningBalanceFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetOwnershipPeriods")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetOwnershipPeriodsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ownership-periods")] HttpRequestData req) =>
        HandleAsync(req, async () => await _useCase.GetOwnershipPeriodsAsync());

    [Function("StartOwnership")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> StartOwnershipAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ownership-periods/start")] HttpRequestData req,
        FunctionContext context) =>
        HandleAsync(req, async () => await _useCase.StartOwnershipAsync(await ReadAsync<StartOwnershipRequest>(req), GetActor(context)));

    [Function("GetOpeningBalances")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "opening-balances")] HttpRequestData req) =>
        HandleAsync(req, async () => await _useCase.ListAsync());

    /// <summary>GET /opening-balances/component-credit-preview?componentId=&amp;date=yyyy-MM-dd&amp;value=-20000</summary>
    [Function("PreviewComponentCredit")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> PreviewComponentCreditAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "opening-balances/component-credit-preview")] HttpRequestData req) =>
        HandleAsync(req, async () =>
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
        HandleAsync(req, async () =>
            await _useCase.CreateAsync(await ReadAsync<SaveOpeningBalanceRequest>(req), GetActor(context)), HttpStatusCode.Created);

    /// <summary>PUT /opening-balances/{key} — key URL-encoded (it contains „|“).</summary>
    [Function("UpdateOpeningBalance")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> UpdateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "opening-balances/{key}")] HttpRequestData req,
        FunctionContext context,
        string key) =>
        HandleAsync(req, async () =>
            await _useCase.UpdateAsync(Uri.UnescapeDataString(key), await ReadAsync<SaveOpeningBalanceRequest>(req), GetActor(context)));

    /// <summary>DELETE /opening-balances/{key}?reason=</summary>
    [Function("DeleteOpeningBalance")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "opening-balances/{key}")] HttpRequestData req,
        FunctionContext context,
        string key) =>
        HandleAsync(req, async () =>
        {
            var reason = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["reason"];
            await _useCase.DeleteAsync(Uri.UnescapeDataString(key), reason, GetActor(context));
            return (object?)null;
        }, HttpStatusCode.NoContent);

    // ───────────────────── Helpers ─────────────────────

    private async Task<HttpResponseData> HandleAsync<T>(HttpRequestData req, Func<Task<T>> action, HttpStatusCode success = HttpStatusCode.OK)
    {
        try
        {
            var result = await action();
            if (success == HttpStatusCode.NoContent)
                return req.CreateResponse(HttpStatusCode.NoContent);
            return await WriteJsonAsync(req, success, result);
        }
        catch (JsonException)
        {
            return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { error = "Neplatné tělo požadavku." });
        }
        catch (BusinessRuleException ex)
        {
            var errors = ex.Errors.Select(message => new { field = string.Empty, message }).ToList();
            return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { error = ex.Message, errors });
        }
        catch (AppException ex)
        {
            return await WriteJsonAsync(req, (HttpStatusCode)ex.StatusCode, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in opening balances endpoint {Url}.", req.Url);
            return await WriteJsonAsync(req, HttpStatusCode.InternalServerError, new { error = "Nastala neočekávaná chyba." });
        }
    }

    private static async Task<T> ReadAsync<T>(HttpRequestData req) where T : class =>
        await JsonSerializer.DeserializeAsync<T>(req.Body, JsonOptions) ?? throw new AppException("Chybí tělo požadavku.");

    private static AuditActor GetActor(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) && userObj is User user)
            return new AuditActor(user.Id, user.Name);
        throw new AppException("Uživatel není přihlášen.", 401);
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(HttpRequestData req, HttpStatusCode statusCode, T body)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }
}
