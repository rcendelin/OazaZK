using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Documents;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>Invoices in Documents without a cost entry (T11). Admin and Accountant.</summary>
public class UnaccountedDocumentsFunctions
{
    private readonly UnaccountedDocumentsUseCase _useCase;
    private readonly ILogger<UnaccountedDocumentsFunctions> _logger;

    public UnaccountedDocumentsFunctions(UnaccountedDocumentsUseCase useCase, ILogger<UnaccountedDocumentsFunctions> logger)
    {
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetUnaccountedDocuments")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/unaccounted")] HttpRequestData req) =>
        ModelEndpoint.HandleAsync(req, _logger, async () => await _useCase.ListAsync());
}
