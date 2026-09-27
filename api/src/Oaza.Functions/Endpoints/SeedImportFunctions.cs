using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Seed;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>
/// Import of the starting data (T13), Admin only and on every environment (not behind ENABLE_SEED): dry run, report
/// download, apply. The body is the uploaded CSVs as <c>{ files: { "houses.csv": "…text…" } }</c>.
/// </summary>
public class SeedImportFunctions
{
    private readonly SeedImportUseCase _useCase;
    private readonly ILogger<SeedImportFunctions> _logger;

    public SeedImportFunctions(SeedImportUseCase useCase, ILogger<SeedImportFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("SeedImportDryRun")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> DryRunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "seed-import/dry-run")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.DryRunAsync(await FilesAsync(req)));

    /// <summary>POST /seed-import/report?format=md|xlsx — the dry-run report as a file.</summary>
    [Function("SeedImportReport")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> ReportAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "seed-import/report")] HttpRequestData req) =>
        ModelEndpoint.HandleFileAsync(req, _logger, async () =>
        {
            var file = SeedReportExport.Build(await _useCase.DryRunAsync(await FilesAsync(req)), ModelEndpoint.Query(req, "format") ?? "md");
            return (file.Content, file.ContentType, file.FileName);
        });

    [Function("SeedImportApply")]
    [RequireRole(UserRole.Admin)]
    public Task<HttpResponseData> ApplyAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "seed-import/apply")] HttpRequestData req,
        FunctionContext context) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.ApplyAsync(await FilesAsync(req), ModelEndpoint.GetActor(context)));

    private static async Task<IReadOnlyDictionary<string, string>> FilesAsync(HttpRequestData req) =>
        (await ModelEndpoint.ReadAsync<SeedImportRequest>(req)).Files ?? [];

    private sealed class SeedImportRequest
    {
        public Dictionary<string, string>? Files { get; set; }
    }
}
