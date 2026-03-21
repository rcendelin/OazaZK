using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
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
    private readonly ILogger<InvoiceFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public InvoiceFunctions(
        ISupplierInvoiceRepository invoiceRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ILogger<InvoiceFunctions> logger)
    {
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetInvoices")]
    public async Task<HttpResponseData> GetInvoicesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "invoices")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            // Only Admin and Accountant can access invoices
            var user = GetAuthenticatedUser(context);
            if (user.Role != UserRole.Admin && user.Role != UserRole.Accountant)
            {
                return await WriteErrorResponseAsync(req, 403, "Insufficient permissions.");
            }

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

            var invoice = new SupplierInvoice
            {
                Id = Guid.NewGuid().ToString(),
                Year = request.Year,
                Month = request.Month,
                InvoiceNumber = request.InvoiceNumber,
                IssuedDate = request.IssuedDate,
                DueDate = request.DueDate,
                Amount = request.Amount,
                ConsumptionM3 = request.ConsumptionM3,
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

            existing.InvoiceNumber = request.InvoiceNumber;
            existing.IssuedDate = request.IssuedDate;
            existing.DueDate = request.DueDate;
            existing.Amount = request.Amount;
            existing.ConsumptionM3 = request.ConsumptionM3;

            // Apply optional year/month change
            if (request.Year.HasValue)
                existing.Year = request.Year.Value;
            if (request.Month.HasValue)
                existing.Month = request.Month.Value;

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
