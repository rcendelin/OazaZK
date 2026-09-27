using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>Water PVK and losses overview (T05). Admin and Accountant; members see their water in the ledger (T07).</summary>
public class WaterSettlementFunctions
{
    private readonly WaterSettlementUseCase _useCase;
    private readonly ILogger<WaterSettlementFunctions> _logger;

    public WaterSettlementFunctions(WaterSettlementUseCase useCase, ILogger<WaterSettlementFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>GET /water-settlement?from=yyyy-MM-dd&amp;to=yyyy-MM-dd</summary>
    [Function("GetWaterSettlement")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "water-settlement")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            if (!ModelEndpoint.TryParseDay(ModelEndpoint.Query(req, "from"), out var from) || !ModelEndpoint.TryParseDay(ModelEndpoint.Query(req, "to"), out var to))
                throw new AppException("Zadejte období parametry from a to ve tvaru RRRR-MM-DD.");
            return await _useCase.CalculateAsync(from, to);
        });
}
