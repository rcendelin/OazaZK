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

/// <summary>Cash book (T09): every signed-in member reads (transparency), Admin and Accountant write and export.</summary>
public class CashBookFunctions
{
    private readonly CashBookUseCase _useCase;
    private readonly ILogger<CashBookFunctions> _logger;

    public CashBookFunctions(CashBookUseCase useCase, ILogger<CashBookFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>GET /cash-book?from=&amp;to=</summary>
    [Function("GetCashBook")]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cash-book")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
        {
            var (from, to) = Range(req);
            return await _useCase.ListAsync(from, to);
        });

    /// <summary>GET /cash-book/export?format=xlsx|pdf&amp;from=&amp;to=</summary>
    [Function("ExportCashBook")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ExportAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "cash-book/export")] HttpRequestData req) =>
        ModelEndpoint.HandleFileAsync(req, _logger, async () =>
        {
            var (from, to) = Range(req);
            var file = await _useCase.ExportAsync(from, to, ModelEndpoint.Query(req, "format") ?? "xlsx");
            return (file.Content, file.ContentType, file.FileName);
        });

    [Function("CreateCashBookEntry")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cash-book")] HttpRequestData req,
        FunctionContext context) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.CreateAsync(await ModelEndpoint.ReadAsync<CreateCashBookEntryRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

    [Function("StornoCashBookEntry")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> StornoAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "cash-book/{id}/storno")] HttpRequestData req,
        FunctionContext context,
        string id) =>
        ModelEndpoint.HandleAsync(req, _logger, async () =>
            await _useCase.StornoAsync(id, await ModelEndpoint.ReadAsync<StornoCashBookEntryRequest>(req), ModelEndpoint.GetActor(context)), HttpStatusCode.Created);

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
}
