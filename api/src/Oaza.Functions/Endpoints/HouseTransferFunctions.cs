using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>„Převod domu“ (T03): preview and the transfer itself. Admin only.</summary>
public class HouseTransferFunctions
{
    private readonly HouseTransferUseCase _useCase;
    private readonly ILogger<HouseTransferFunctions> _logger;

    public HouseTransferFunctions(HouseTransferUseCase useCase, ILogger<HouseTransferFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>GET /houses/{id}/transfer-preview?date=yyyy-MM-dd</summary>
    [Function("PreviewHouseTransfer")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> PreviewAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "houses/{id}/transfer-preview")] HttpRequestData req,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            if (!ModelEndpoint.TryParseDay(ModelEndpoint.Query(req, "date"), out var date))
                throw new AppException("Zadejte datum převodu ve tvaru RRRR-MM-DD.");
            return await _useCase.PreviewAsync(id, date);
        });

    [Function("TransferHouse")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> TransferAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "houses/{id}/transfer")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.TransferAsync(id, await ModelEndpoint.ReadAsync<HouseTransferRequest>(req), ModelEndpoint.GetActor(context)));
}
