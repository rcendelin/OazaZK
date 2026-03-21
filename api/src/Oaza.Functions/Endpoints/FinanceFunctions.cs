using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Mapping;
using Oaza.Application.UseCases;
using Oaza.Application.Validators;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class FinanceFunctions
{
    private readonly IFinancialRecordRepository _financialRecordRepository;
    private readonly GenerateFinanceReportUseCase _generatePdfUseCase;
    private readonly GenerateFinanceExcelUseCase _generateExcelUseCase;
    private readonly ILogger<FinanceFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public FinanceFunctions(
        IFinancialRecordRepository financialRecordRepository,
        GenerateFinanceReportUseCase generatePdfUseCase,
        GenerateFinanceExcelUseCase generateExcelUseCase,
        ILogger<FinanceFunctions> logger)
    {
        _financialRecordRepository = financialRecordRepository ?? throw new ArgumentNullException(nameof(financialRecordRepository));
        _generatePdfUseCase = generatePdfUseCase ?? throw new ArgumentNullException(nameof(generatePdfUseCase));
        _generateExcelUseCase = generateExcelUseCase ?? throw new ArgumentNullException(nameof(generateExcelUseCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetFinancialRecords")]
    public async Task<HttpResponseData> GetFinancialRecordsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Unauthorized");

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var yearParam = queryParams["year"];
            var category = queryParams["category"];

            IReadOnlyList<FinancialRecord> records;

            if (int.TryParse(yearParam, out var year) && !string.IsNullOrEmpty(category))
            {
                records = await _financialRecordRepository.GetByYearAndCategoryAsync(year, category);
            }
            else if (int.TryParse(yearParam, out year))
            {
                records = await _financialRecordRepository.GetByYearAsync(year);
            }
            else
            {
                records = await _financialRecordRepository.GetAllAsync();
            }

            var responses = records.Select(EntityMapper.ToResponse).ToList();
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

    [Function("GetFinanceSummary")]
    public async Task<HttpResponseData> GetFinanceSummaryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance/summary")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Unauthorized");

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var yearParam = queryParams["year"];

            if (!int.TryParse(yearParam, out var year))
            {
                return await WriteErrorResponseAsync(req, 400, "Query parameter 'year' is required and must be a valid integer.");
            }

            var records = await _financialRecordRepository.GetByYearAsync(year);

            var totalIncome = records
                .Where(r => r.Type == FinancialRecordType.Income)
                .Sum(r => r.Amount);

            var totalExpenses = records
                .Where(r => r.Type == FinancialRecordType.Expense)
                .Sum(r => r.Amount);

            var categories = records
                .GroupBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CategorySummary(
                    Category: g.Key,
                    Income: g.Where(r => r.Type == FinancialRecordType.Income).Sum(r => r.Amount),
                    Expenses: g.Where(r => r.Type == FinancialRecordType.Expense).Sum(r => r.Amount)))
                .ToList();

            var summary = new FinanceSummaryResponse(
                Year: year,
                TotalIncome: totalIncome,
                TotalExpenses: totalExpenses,
                Balance: totalIncome - totalExpenses,
                Categories: categories);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, summary);
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

    [Function("CreateFinancialRecord")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateFinancialRecordAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "finance")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateFinanceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new CreateFinanceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var recordType = Enum.Parse<FinancialRecordType>(request.Type, ignoreCase: true);

            var record = new FinancialRecord
            {
                Id = Guid.NewGuid().ToString(),
                Year = request.Date.Year,
                Type = recordType,
                Category = request.Category.ToLowerInvariant(),
                Amount = request.Amount,
                Date = request.Date,
                Description = request.Description,
            };

            await _financialRecordRepository.UpsertAsync(record);

            _logger.LogInformation("Financial record {RecordId} created: {Type} {Category} {Amount} CZK.",
                record.Id, record.Type, record.Category, record.Amount);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(record));
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

    [Function("UpdateFinancialRecord")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateFinancialRecordAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "finance/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            // Financial records use PK = year, RK = id
            // We need to find the record first — try recent years
            var existing = await FindFinancialRecordByIdAsync(id);
            if (existing is null)
            {
                throw new NotFoundException("FinancialRecord", id);
            }

            var request = await JsonSerializer.DeserializeAsync<UpdateFinanceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new UpdateFinanceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var newYear = request.Date.Year;
            var oldYear = existing.Year;

            var recordType = Enum.Parse<FinancialRecordType>(request.Type, ignoreCase: true);

            existing.Type = recordType;
            existing.Category = request.Category.ToLowerInvariant();
            existing.Amount = request.Amount;
            existing.Date = request.Date;
            existing.Description = request.Description;

            if (newYear != oldYear)
            {
                // Write new record first (safer — duplicate is recoverable, loss is not)
                existing.Year = newYear;
                await _financialRecordRepository.UpsertAsync(existing);
                // Then delete old partition key entry
                await _financialRecordRepository.DeleteAsync(oldYear.ToString(), id);
            }
            else
            {
                await _financialRecordRepository.UpsertAsync(existing);
            }

            _logger.LogInformation("Financial record {RecordId} updated.", id);

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

    [Function("ExportFinancePdf")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public async Task<HttpResponseData> ExportFinancePdfAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance/export/pdf")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Unauthorized");

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var yearParam = queryParams["year"];

            if (!int.TryParse(yearParam, out var year))
                return await WriteErrorResponseAsync(req, 400, "Query parameter 'year' is required and must be a valid integer.");

            var records = await _financialRecordRepository.GetByYearAsync(year);
            var pdfBytes = _generatePdfUseCase.Generate(year, records);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/pdf");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"hospodareni-{year}.pdf\"");
            response.Body = new MemoryStream(pdfBytes);
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

    [Function("ExportFinanceExcel")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public async Task<HttpResponseData> ExportFinanceExcelAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance/export/xlsx")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Unauthorized");

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var yearParam = queryParams["year"];

            if (!int.TryParse(yearParam, out var year))
                return await WriteErrorResponseAsync(req, 400, "Query parameter 'year' is required and must be a valid integer.");

            var records = await _financialRecordRepository.GetByYearAsync(year);
            var excelBytes = _generateExcelUseCase.Generate(year, records);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"hospodareni-{year}.xlsx\"");
            response.Body = new MemoryStream(excelBytes);
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

    /// <summary>
    /// Find a financial record by ID. Since PK = year, we search recent years.
    /// </summary>
    private async Task<FinancialRecord?> FindFinancialRecordByIdAsync(string id)
    {
        // Search current year and a few years back
        var currentYear = DateTime.UtcNow.Year;
        for (var year = currentYear + 1; year >= currentYear - 10; year--)
        {
            var record = await _financialRecordRepository.GetAsync(year.ToString(), id);
            if (record is not null)
            {
                return record;
            }
        }
        return null;
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

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) &&
            userObj is User user)
        {
            return user;
        }

        throw new AppException("User not authenticated.", 401);
    }
}
