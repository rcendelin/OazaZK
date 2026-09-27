using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Exceptions;
using Oaza.Application.Readings;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>Readings export with the estimate flag (T04). Admin and Accountant.</summary>
public class ReadingExportFunctions
{
    private readonly ReadingsExportUseCase _useCase;
    private readonly ILogger<ReadingExportFunctions> _logger;

    public ReadingExportFunctions(ReadingsExportUseCase useCase, ILogger<ReadingExportFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>GET /readings/export?format=xlsx|csv&amp;from=&amp;to= — both days optional (yyyy-MM-dd).</summary>
    [Function("ExportReadings")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ExportAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "readings/export")] HttpRequestData req) =>
        ModelEndpoint.HandleFileAsync(req, _logger, async () =>
        {
            var file = await _useCase.ExportAsync(Day(req, "from"), Day(req, "to"), ModelEndpoint.Query(req, "format") ?? "xlsx");
            return (file.Content, file.ContentType, file.FileName);
        });

    private static DateOnly? Day(HttpRequestData req, string name)
    {
        var text = ModelEndpoint.Query(req, name);
        if (string.IsNullOrEmpty(text))
            return null;
        return ModelEndpoint.TryParseDay(text, out var day) ? day : throw new AppException($"Parametr {name} zadejte ve tvaru RRRR-MM-DD.");
    }
}
