using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.Exceptions;
using Oaza.Application.Ledger;
using Oaza.Domain.Entities;

namespace Oaza.Functions.Endpoints;

/// <summary>
/// House ledger and overview (T07). Any signed-in user: a member sees the detail of their own house only and the
/// overview of all houses without personal data; Admin and Accountant see everything.
/// </summary>
public class LedgerFunctions
{
    private readonly HouseLedgerUseCase _useCase;
    private readonly ILogger<LedgerFunctions> _logger;

    public LedgerFunctions(HouseLedgerUseCase useCase, ILogger<LedgerFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>GET /ledger/houses/{houseId}?ownershipPeriodId=&amp;from=&amp;to=</summary>
    [Function("GetHouseLedger")]
    public Task<HttpResponseData> GetHouseLedgerAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ledger/houses/{houseId}")] HttpRequestData req,
        FunctionContext context,
        string houseId) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            var (from, to) = Range(req);
            return await _useCase.GetHouseLedgerAsync(houseId, ModelEndpoint.Query(req, "ownershipPeriodId"), from, to, Requester(context));
        });

    /// <summary>GET /ledger/overview?from=&amp;to=</summary>
    [Function("GetLedgerOverview")]
    public Task<HttpResponseData> GetOverviewAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ledger/overview")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            var (from, to) = Range(req);
            return await _useCase.GetOverviewAsync(from, to);
        });

    private static (DateOnly? From, DateOnly? To) Range(HttpRequestData req)
    {
        DateOnly? Parse(string name)
        {
            var text = ModelEndpoint.Query(req, name);
            if (string.IsNullOrEmpty(text))
                return null;
            return ModelEndpoint.TryParseDay(text, out var day) ? day : throw new AppException($"Parametr {name} zadejte ve tvaru RRRR-MM-DD.");
        }
        return (Parse("from"), Parse("to"));
    }

    private static LedgerRequester Requester(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) && userObj is User user)
            return new LedgerRequester(user.Role, user.HouseId);
        throw new AppException("Uživatel není přihlášen.", 401);
    }
}
