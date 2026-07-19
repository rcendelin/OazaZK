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
using Oaza.Application.UseCases;
using Oaza.Application.Validators;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class FinanceFunctions
{
    private readonly IFinancialRecordRepository _financialRecordRepository;
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly GetFundBalanceUseCase _getFundBalanceUseCase;
    private readonly GenerateFinanceReportUseCase _generatePdfUseCase;
    private readonly GenerateFinanceExcelUseCase _generateExcelUseCase;
    private readonly ILogger<FinanceFunctions> _logger;

    private const long MaxAttachmentBytes = 20 * 1024 * 1024; // 20 MB

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public FinanceFunctions(
        IFinancialRecordRepository financialRecordRepository,
        IAdvancePaymentRepository advanceRepository,
        IHouseRepository houseRepository,
        IBlobStorageService blobStorageService,
        GetFundBalanceUseCase getFundBalanceUseCase,
        GenerateFinanceReportUseCase generatePdfUseCase,
        GenerateFinanceExcelUseCase generateExcelUseCase,
        ILogger<FinanceFunctions> logger)
    {
        _financialRecordRepository = financialRecordRepository ?? throw new ArgumentNullException(nameof(financialRecordRepository));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _getFundBalanceUseCase = getFundBalanceUseCase ?? throw new ArgumentNullException(nameof(getFundBalanceUseCase));
        _generatePdfUseCase = generatePdfUseCase ?? throw new ArgumentNullException(nameof(generatePdfUseCase));
        _generateExcelUseCase = generateExcelUseCase ?? throw new ArgumentNullException(nameof(generateExcelUseCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetFundBalance")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public async Task<HttpResponseData> GetFundBalanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance/fund")] HttpRequestData req)
    {
        try
        {
            var result = await _getFundBalanceUseCase.CalculateAsync();
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, new
            {
                commonContributions = result.CommonContributions,
                extraordinaryCosts = result.ExtraordinaryCosts,
                fundBalance = result.FundBalance,
            });
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
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
                return await WriteErrorResponseAsync(req, 401, "Nejste přihlášeni.");

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
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
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
                return await WriteErrorResponseAsync(req, 401, "Nejste přihlášeni.");

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var yearParam = queryParams["year"];

            if (!int.TryParse(yearParam, out var year))
            {
                return await WriteErrorResponseAsync(req, 400, "Parametr 'year' je povinný a musí být platné celé číslo.");
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
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("GetFinanceBalance")]
    public async Task<HttpResponseData> GetFinanceBalanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance/balance")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            if (user is null)
                return await WriteErrorResponseAsync(req, 401, "Nejste přihlášeni.");

            var allRecords = await _financialRecordRepository.GetAllAsync();

            var totalIncome = allRecords
                .Where(r => r.Type == FinancialRecordType.Income)
                .Sum(r => r.Amount);

            var totalExpenses = allRecords
                .Where(r => r.Type == FinancialRecordType.Expense)
                .Sum(r => r.Amount);

            var response = new FinanceBalanceResponse(
                TotalIncome: totalIncome,
                TotalExpenses: totalExpenses,
                Balance: totalIncome - totalExpenses);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, response);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
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
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            }

            var validator = new CreateFinanceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            if (!Enum.TryParse<FinancialRecordType>(request.Type, ignoreCase: true, out var recordType))
                return await WriteErrorResponseAsync(req, 400, "Neplatný typ záznamu.");

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
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
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
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            }

            var validator = new UpdateFinanceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var newYear = request.Date.Year;
            var oldYear = existing.Year;

            if (!Enum.TryParse<FinancialRecordType>(request.Type, ignoreCase: true, out var recordType))
                return await WriteErrorResponseAsync(req, 400, "Neplatný typ záznamu.");

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
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
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
                return await WriteErrorResponseAsync(req, 401, "Nejste přihlášeni.");

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var yearParam = queryParams["year"];

            if (!int.TryParse(yearParam, out var year))
                return await WriteErrorResponseAsync(req, 400, "Parametr 'year' je povinný a musí být platné celé číslo.");

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
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
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
                return await WriteErrorResponseAsync(req, 401, "Nejste přihlášeni.");

            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var yearParam = queryParams["year"];

            if (!int.TryParse(yearParam, out var year))
                return await WriteErrorResponseAsync(req, 400, "Parametr 'year' je povinný a musí být platné celé číslo.");

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
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("UploadFinanceAttachment")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UploadFinanceAttachmentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "finance/{id}/attachment")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await FindFinancialRecordByIdAsync(id);
            if (existing is null)
            {
                throw new NotFoundException("FinancialRecord", id);
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

            var blobPath = $"{id}/faktura.pdf";
            await _blobStorageService.UploadAsync(BlobContainerNames.Finance, blobPath, bytes, "application/pdf");

            existing.AttachmentBlobName = blobPath;
            await _financialRecordRepository.UpsertAsync(existing);

            _logger.LogInformation("Attachment uploaded for finance record {RecordId} ({Size} bytes).", id, bytes.Length);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, EntityMapper.ToResponse(existing));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("DownloadFinanceAttachment")]
    [RequireRole(UserRole.Admin, UserRole.Accountant)]
    public async Task<HttpResponseData> DownloadFinanceAttachmentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "finance/{id}/attachment")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await FindFinancialRecordByIdAsync(id);
            if (existing is null)
            {
                throw new NotFoundException("FinancialRecord", id);
            }

            if (string.IsNullOrEmpty(existing.AttachmentBlobName))
            {
                return await WriteErrorResponseAsync(req, 404, "Záznam nemá přílohu.");
            }

            var stream = await _blobStorageService.DownloadAsync(BlobContainerNames.Finance, existing.AttachmentBlobName);
            if (stream is null)
            {
                return await WriteErrorResponseAsync(req, 404, "Soubor nebyl ve storage nalezen.");
            }

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/pdf");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"faktura-{id}.pdf\"");
            response.Body = new MemoryStream(ms.ToArray());
            return response;
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
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
            new { error = "Formulář obsahuje chyby.", errors });
    }

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) &&
            userObj is User user)
        {
            return user;
        }

        throw new AppException("Uživatel není přihlášen.", 401);
    }
}
