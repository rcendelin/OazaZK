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
/// Cost components, allocation rules and participation (T02). Admin and Accountant read,
/// only Admin writes. Business rule violations answer 400 with every reason in <c>errors</c>.
/// </summary>
public class CostComponentFunctions
{
    private readonly CostComponentsUseCase _useCase;
    private readonly ILogger<CostComponentFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public CostComponentFunctions(CostComponentsUseCase useCase, ILogger<CostComponentFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetCostComponents")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cost-components")] HttpRequestData req) =>
        HandleAsync(req, async () => await _useCase.ListAsync());

    [Function("GetCostComponent")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cost-components/{id}")] HttpRequestData req,
        string id) =>
        HandleAsync(req, async () => await _useCase.GetDetailAsync(id));

    /// <summary>GET /cost-components/{id}/segments?from=yyyy-MM-dd&amp;to=yyyy-MM-dd</summary>
    [Function("GetCostComponentSegments")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetSegmentsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cost-components/{id}/segments")] HttpRequestData req,
        string id) =>
        HandleAsync(req, async () =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            if (!TryParseDay(query["from"], out var from) || !TryParseDay(query["to"], out var to))
                throw new AppException("Zadejte období parametry from a to ve tvaru RRRR-MM-DD.");
            return await _useCase.GetSegmentsAsync(id, from, to);
        });

    [Function("CreateCostComponent")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components")] HttpRequestData req,
        FunctionContext context) =>
        HandleAsync(req, async () =>
            await _useCase.CreateAsync(await ReadAsync<CreateCostComponentRequest>(req), GetActor(context)), HttpStatusCode.Created);

    [Function("UpdateCostComponent")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> UpdateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "cost-components/{id}")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        HandleAsync(req, async () =>
            await _useCase.UpdateAsync(id, await ReadAsync<UpdateCostComponentRequest>(req), GetActor(context)));

    [Function("AddCostComponentRule")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> AddRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components/{id}/rules")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        HandleAsync(req, async () =>
            await _useCase.AddRuleAsync(id, await ReadAsync<AddAllocationRuleRequest>(req), GetActor(context)), HttpStatusCode.Created);

    /// <summary>DELETE /cost-components/{id}/rules/{ruleId}?reason=</summary>
    [Function("DeleteCostComponentRule")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteRuleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "cost-components/{id}/rules/{ruleId}")] HttpRequestData req,
        FunctionContext context,
        string id,
        string ruleId) =>
        HandleAsync(req, async () =>
        {
            await _useCase.DeleteRuleAsync(id, ruleId, ReasonFromQuery(req), GetActor(context));
            return (object?)null;
        }, HttpStatusCode.NoContent);

    [Function("AddCostComponentParticipation")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> AddParticipationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components/{id}/participations")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        HandleAsync(req, async () =>
            await _useCase.AddParticipationAsync(id, await ReadAsync<AddParticipationRequest>(req), GetActor(context)), HttpStatusCode.Created);

    [Function("EndCostComponentParticipation")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> EndParticipationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cost-components/{id}/participations/{participationId}/end")] HttpRequestData req,
        FunctionContext context,
        string id,
        string participationId) =>
        HandleAsync(req, async () =>
            await _useCase.EndParticipationAsync(id, participationId, await ReadAsync<EndParticipationRequest>(req), GetActor(context)));

    /// <summary>DELETE /cost-components/{id}/participations/{participationId}?reason=</summary>
    [Function("DeleteCostComponentParticipation")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DeleteParticipationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "cost-components/{id}/participations/{participationId}")] HttpRequestData req,
        FunctionContext context,
        string id,
        string participationId) =>
        HandleAsync(req, async () =>
        {
            await _useCase.DeleteParticipationAsync(id, participationId, ReasonFromQuery(req), GetActor(context));
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
            _logger.LogError(ex, "Unexpected error in cost components endpoint {Url}.", req.Url);
            return await WriteJsonAsync(req, HttpStatusCode.InternalServerError, new { error = "Nastala neočekávaná chyba." });
        }
    }

    private static async Task<T> ReadAsync<T>(HttpRequestData req) where T : class =>
        await JsonSerializer.DeserializeAsync<T>(req.Body, JsonOptions) ?? throw new AppException("Chybí tělo požadavku.");

    private static string? ReasonFromQuery(HttpRequestData req) =>
        System.Web.HttpUtility.ParseQueryString(req.Url.Query)["reason"];

    private static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

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
