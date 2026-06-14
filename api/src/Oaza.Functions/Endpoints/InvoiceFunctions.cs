using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Application.Mapping;
using Oaza.Application.Validators;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class InvoiceFunctions
{
    private readonly ISupplierInvoiceRepository _invoiceRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<InvoiceFunctions> _logger;

    private const long MaxAttachmentBytes = 20 * 1024 * 1024; // 20 MB

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public InvoiceFunctions(
        ISupplierInvoiceRepository invoiceRepository,
        IBillingPeriodRepository billingPeriodRepository,
        IBlobStorageService blobStorageService,
        ILogger<InvoiceFunctions> logger)
    {
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetInvoices")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public async Task<HttpResponseData> GetInvoicesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "invoices")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            // Role enforced centrally by [RequireRole] via AuthorizationMiddleware.
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var yearParam = queryParams["year"];

            IReadOnlyList<SupplierInvoice> invoices;
            if (int.TryParse(yearParam, out var year))
            {
                invoices = await _invoiceRepository.GetByYearAsync(year);
            }
            else
            {
                invoices = await _invoiceRepository.GetByPartitionKeyAsync(PartitionKeys.Invoice);
            }

            var responses = invoices.Select(EntityMapper.ToResponse).ToList();
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, responses);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("CreateInvoice")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateInvoiceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "invoices")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateInvoiceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new CreateInvoiceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var lineItems = request.LineItems.Select(ToLineItem).ToList();
            var firstFrom = lineItems.Min(l => l.DateFrom);

            var invoice = new SupplierInvoice
            {
                Id = Guid.NewGuid().ToString(),
                Year = firstFrom.Year,
                Month = firstFrom.Month,
                InvoiceNumber = request.InvoiceNumber,
                IssuedDate = request.IssuedDate,
                DueDate = request.DueDate,
                VatRatePercent = request.VatRatePercent,
                LineItems = lineItems,
                ConsumptionM3 = lineItems.Sum(l => l.ConsumptionM3),
                Amount = TotalInclVat(lineItems, request.VatRatePercent),
            };

            await _invoiceRepository.UpsertAsync(invoice);

            _logger.LogInformation("Invoice {InvoiceId} created: {InvoiceNumber} for {Year}-{Month}.",
                invoice.Id, invoice.InvoiceNumber, invoice.Year, invoice.Month);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(invoice));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("UpdateInvoice")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateInvoiceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "invoices/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await _invoiceRepository.GetAsync(PartitionKeys.Invoice, id);
            if (existing is null)
            {
                throw new NotFoundException("Invoice", id);
            }

            // Check if invoice is in a closed billing period
            if (await IsInvoiceInClosedPeriodAsync(existing))
            {
                return await WriteErrorResponseAsync(req, 409, "Cannot modify an invoice in a closed billing period.");
            }

            var request = await JsonSerializer.DeserializeAsync<UpdateInvoiceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new UpdateInvoiceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var lineItems = request.LineItems.Select(ToLineItem).ToList();
            var firstFrom = lineItems.Min(l => l.DateFrom);

            existing.InvoiceNumber = request.InvoiceNumber;
            existing.IssuedDate = request.IssuedDate;
            existing.DueDate = request.DueDate;
            existing.VatRatePercent = request.VatRatePercent;
            existing.LineItems = lineItems;
            existing.Year = firstFrom.Year;
            existing.Month = firstFrom.Month;
            existing.ConsumptionM3 = lineItems.Sum(l => l.ConsumptionM3);
            existing.Amount = TotalInclVat(lineItems, request.VatRatePercent);

            // Check if the new year/month would fall into a closed period
            if (await IsInvoiceInClosedPeriodAsync(existing))
            {
                return await WriteErrorResponseAsync(req, 409, "Cannot move an invoice into a closed billing period.");
            }

            await _invoiceRepository.UpsertAsync(existing);

            _logger.LogInformation("Invoice {InvoiceId} updated.", id);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                EntityMapper.ToResponse(existing));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("DeleteInvoice")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> DeleteInvoiceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "invoices/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await _invoiceRepository.GetAsync(PartitionKeys.Invoice, id);
            if (existing is null)
            {
                throw new NotFoundException("Invoice", id);
            }

            // Check if invoice is in a closed billing period
            if (await IsInvoiceInClosedPeriodAsync(existing))
            {
                return await WriteErrorResponseAsync(req, 409, "Cannot delete an invoice in a closed billing period.");
            }

            await _invoiceRepository.DeleteAsync(PartitionKeys.Invoice, id);

            _logger.LogInformation("Invoice {InvoiceId} deleted.", id);

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("UploadInvoiceAttachment")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UploadInvoiceAttachmentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "invoices/{id}/attachment")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await _invoiceRepository.GetAsync(PartitionKeys.Invoice, id);
            if (existing is null)
            {
                throw new NotFoundException("Invoice", id);
            }

            if (await IsInvoiceInClosedPeriodAsync(existing))
            {
                return await WriteErrorResponseAsync(req, 409, "Cannot modify an invoice in a closed billing period.");
            }

            var contentType = req.Headers.TryGetValues("Content-Type", out var ctValues)
                ? ctValues.FirstOrDefault() ?? string.Empty
                : string.Empty;
            if (!contentType.Contains("application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                return await WriteErrorResponseAsync(req, 400, "Příloha musí být ve formátu PDF.");
            }

            var bytes = await ReadBodyBytesWithLimitAsync(req.Body, MaxAttachmentBytes);
            if (bytes.Length == 0)
            {
                return await WriteErrorResponseAsync(req, 400, "Prázdný soubor.");
            }

            var blobPath = $"{id}/faktura-{existing.Year}-{existing.Month:D2}.pdf";
            await _blobStorageService.UploadAsync(BlobContainerNames.Invoices, blobPath, bytes, "application/pdf");

            existing.AttachmentBlobName = blobPath;
            await _invoiceRepository.UpsertAsync(existing);

            _logger.LogInformation("Attachment uploaded for invoice {InvoiceId} ({Size} bytes).", id, bytes.Length);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, EntityMapper.ToResponse(existing));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("DownloadInvoiceAttachment")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public async Task<HttpResponseData> DownloadInvoiceAttachmentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "invoices/{id}/attachment")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await _invoiceRepository.GetAsync(PartitionKeys.Invoice, id);
            if (existing is null)
            {
                throw new NotFoundException("Invoice", id);
            }

            if (string.IsNullOrEmpty(existing.AttachmentBlobName))
            {
                return await WriteErrorResponseAsync(req, 404, "Faktura nemá přílohu.");
            }

            var stream = await _blobStorageService.DownloadAsync(BlobContainerNames.Invoices, existing.AttachmentBlobName);
            if (stream is null)
            {
                return await WriteErrorResponseAsync(req, 404, "Soubor nebyl ve storage nalezen.");
            }

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/pdf");
            var fileName = $"faktura-{SanitizeFileName(existing.InvoiceNumber)}.pdf";
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            response.Body = new MemoryStream(ms.ToArray());
            return response;
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    private static async Task<byte[]> ReadBodyBytesWithLimitAsync(Stream body, long limit)
    {
        using var ms = new MemoryStream();
        await body.CopyToAsync(ms);
        if (ms.Length > limit)
        {
            throw new AppException("Soubor je příliš velký (max 20 MB).", 400);
        }
        return ms.ToArray();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "faktura" : clean;
    }

    private static InvoiceLineItem ToLineItem(InvoiceLineItemDto d) => new()
    {
        DateFrom = DateTime.SpecifyKind(d.DateFrom, DateTimeKind.Utc),
        DateTo = DateTime.SpecifyKind(d.DateTo, DateTimeKind.Utc),
        StartReading = d.StartReading,
        EndReading = d.EndReading,
        ConsumptionM3 = d.ConsumptionM3,
        UnitPrice = d.UnitPrice,
        AmountExclVat = d.AmountExclVat,
    };

    private static decimal TotalInclVat(IEnumerable<InvoiceLineItem> lines, decimal vatRatePercent) =>
        Math.Round(lines.Sum(l => l.AmountExclVat) * (1m + vatRatePercent / 100m), 2);

    private async Task<bool> IsInvoiceInClosedPeriodAsync(SupplierInvoice invoice)
    {
        var periods = await _billingPeriodRepository.GetByPartitionKeyAsync(PartitionKeys.Period);
        var invoiceDate = new DateTime(invoice.Year, invoice.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return periods.Any(p =>
            p.Status == BillingPeriodStatus.Closed &&
            invoiceDate >= p.DateFrom &&
            invoiceDate <= p.DateTo);
    }

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) &&
            userObj is User user)
        {
            return user;
        }

        throw new AppException("User not authenticated.", 401);
    }

    private static async Task<HttpResponseData> WriteJsonResponseAsync<T>(
        HttpRequestData req, HttpStatusCode statusCode, T body)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }

    private static async Task<HttpResponseData> WriteErrorResponseAsync(HttpRequestData req, int statusCode, string message)
    {
        return await WriteJsonResponseAsync(req, (HttpStatusCode)statusCode, new { error = message });
    }

    private static async Task<HttpResponseData> WriteValidationErrorResponseAsync(
        HttpRequestData req, FluentValidation.Results.ValidationResult validationResult)
    {
        var errors = validationResult.Errors
            .Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
            .ToList();

        return await WriteJsonResponseAsync(req, HttpStatusCode.BadRequest,
            new { error = "Validation failed.", errors });
    }
}
