using System.IO.Compression;
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

public class BillingPeriodFunctions
{
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ISupplierInvoiceRepository _invoiceRepository;
    private readonly ISettlementRepository _settlementRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly CalculateSettlementUseCase _calculateSettlementUseCase;
    private readonly GenerateSettlementPdfUseCase _generateSettlementPdfUseCase;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<BillingPeriodFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public BillingPeriodFunctions(
        IBillingPeriodRepository billingPeriodRepository,
        ISupplierInvoiceRepository invoiceRepository,
        ISettlementRepository settlementRepository,
        IHouseRepository houseRepository,
        CalculateSettlementUseCase calculateSettlementUseCase,
        GenerateSettlementPdfUseCase generateSettlementPdfUseCase,
        IBlobStorageService blobStorageService,
        ILogger<BillingPeriodFunctions> logger)
    {
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
        _settlementRepository = settlementRepository ?? throw new ArgumentNullException(nameof(settlementRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _calculateSettlementUseCase = calculateSettlementUseCase ?? throw new ArgumentNullException(nameof(calculateSettlementUseCase));
        _generateSettlementPdfUseCase = generateSettlementPdfUseCase ?? throw new ArgumentNullException(nameof(generateSettlementPdfUseCase));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetBillingPeriods")]
    public async Task<HttpResponseData> GetBillingPeriodsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing-periods")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var periods = await _billingPeriodRepository.GetByPartitionKeyAsync(PartitionKeys.Period);

            // Get all invoices to compute totals per period
            var allInvoices = await _invoiceRepository.GetByPartitionKeyAsync(PartitionKeys.Invoice);

            var responses = new List<BillingPeriodResponse>();
            foreach (var period in periods)
            {
                // Compute total invoice amount: SUM of invoices where invoice month falls within period
                var periodInvoices = allInvoices.Where(i =>
                {
                    var invoiceDate = new DateTime(i.Year, i.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    return invoiceDate >= period.DateFrom && invoiceDate <= period.DateTo;
                });

                var totalAmount = periodInvoices.Sum(i => i.Amount);
                responses.Add(EntityMapper.ToResponse(period, totalAmount));
            }

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

    [Function("CreateBillingPeriod")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateBillingPeriodAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "billing-periods")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var request = await JsonSerializer.DeserializeAsync<CreateBillingPeriodRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new CreateBillingPeriodRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var period = new BillingPeriod
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                DateFrom = DateTime.SpecifyKind(request.DateFrom, DateTimeKind.Utc),
                DateTo = DateTime.SpecifyKind(request.DateTo, DateTimeKind.Utc),
                Status = BillingPeriodStatus.Open,
            };

            await _billingPeriodRepository.UpsertAsync(period);

            _logger.LogInformation("Billing period {PeriodId} created: {Name}.", period.Id, period.Name);

            // Compute total invoice amount for response
            var invoices = await _invoiceRepository.GetByPeriodAsync(period.DateFrom, period.DateTo);
            var totalAmount = invoices.Sum(i => i.Amount);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(period, totalAmount));
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

    [Function("CalculateSettlement")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CalculateSettlementAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing-periods/{id}/calculate")] HttpRequestData req,
        FunctionContext context,
        string id)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            // Parse loss allocation method from query string (default: Equal)
            var methodParam = req.Url.Query?.Contains("method=") == true
                ? System.Web.HttpUtility.ParseQueryString(req.Url.Query).Get("method")
                : null;

            var lossMethod = LossAllocationMethod.Equal;
            if (!string.IsNullOrEmpty(methodParam) &&
                Enum.TryParse<LossAllocationMethod>(methodParam, ignoreCase: true, out var parsed))
            {
                lossMethod = parsed;
            }

            var preview = await _calculateSettlementUseCase.CalculateAsync(id, lossMethod);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, preview);
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

    [Function("CloseBillingPeriod")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CloseBillingPeriodAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "billing-periods/{id}/close")] HttpRequestData req,
        FunctionContext context,
        string id)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            // Parse loss allocation method from request body
            var request = await JsonSerializer.DeserializeAsync<CalculateSettlementRequest>(req.Body, JsonOptions);
            var lossMethod = LossAllocationMethod.Equal;
            if (request is not null &&
                !string.IsNullOrEmpty(request.LossAllocationMethod) &&
                Enum.TryParse<LossAllocationMethod>(request.LossAllocationMethod, ignoreCase: true, out var parsed))
            {
                lossMethod = parsed;
            }

            // Calculate final settlement numbers
            var preview = await _calculateSettlementUseCase.CalculateAsync(id, lossMethod);

            // Re-verify period is still Open (double-check after calculation)
            var period = await _billingPeriodRepository.GetAsync(PartitionKeys.Period, id);
            if (period is null)
            {
                throw new NotFoundException("BillingPeriod", id);
            }

            if (period.Status != BillingPeriodStatus.Open)
            {
                throw new AppException("Billing period is already closed. Cannot close again.");
            }

            // Save settlement entities for each house
            var settlements = new List<Settlement>();
            foreach (var houseDetail in preview.Houses)
            {
                var settlement = new Settlement
                {
                    PeriodId = id,
                    HouseId = houseDetail.HouseId,
                    ConsumptionM3 = houseDetail.ConsumptionM3,
                    SharePercent = houseDetail.SharePercent,
                    CalculatedAmount = houseDetail.CalculatedAmount,
                    TotalAdvances = houseDetail.TotalAdvances,
                    Balance = houseDetail.Balance,
                    LossAllocatedM3 = houseDetail.LossAllocatedM3,
                };

                await _settlementRepository.UpsertAsync(settlement);
                settlements.Add(settlement);
            }

            // Close the period (irreversible)
            period.Status = BillingPeriodStatus.Closed;
            await _billingPeriodRepository.UpsertAsync(period);

            _logger.LogInformation(
                "Billing period {PeriodId} ({Name}) closed with {Count} settlements.",
                id, period.Name, settlements.Count);

            // Build response with house names
            var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
            var houseNameMap = houses.ToDictionary(h => h.Id, h => h.Name);

            var responses = settlements.Select(s =>
                EntityMapper.ToResponse(s, houseNameMap.GetValueOrDefault(s.HouseId, "Unknown")))
                .ToList();

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

    [Function("GetSettlements")]
    public async Task<HttpResponseData> GetSettlementsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing-periods/{id}/settlements")] HttpRequestData req,
        FunctionContext context,
        string id)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var settlements = await _settlementRepository.GetByPeriodIdAsync(id);

            // If member, filter to own house only
            if (user.Role == UserRole.Member)
            {
                settlements = settlements
                    .Where(s => s.HouseId == user.HouseId)
                    .ToList();
            }

            // Enrich with house names
            var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
            var houseNameMap = houses.ToDictionary(h => h.Id, h => h.Name);

            var responses = settlements.Select(s =>
                EntityMapper.ToResponse(s, houseNameMap.GetValueOrDefault(s.HouseId, "Unknown")))
                .ToList();

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

    [Function("GetSettlementPdf")]
    public async Task<HttpResponseData> GetSettlementPdfAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing-periods/{id}/settlements/{houseId}/pdf")] HttpRequestData req,
        FunctionContext context,
        string id,
        string houseId)
    {
        try
        {
            if (!Guid.TryParse(id, out _) || !Guid.TryParse(houseId, out _))
                return await WriteErrorResponseAsync(req, 400, "Invalid ID format.");

            var user = GetAuthenticatedUser(context);

            // Members can only access their own house's PDF
            if (user.Role == UserRole.Member && user.HouseId != houseId)
            {
                return await WriteErrorResponseAsync(req, 403, "You can only access your own house's settlement PDF.");
            }

            // Check if period exists and is closed
            var period = await _billingPeriodRepository.GetAsync(PartitionKeys.Period, id);
            if (period is null)
            {
                throw new NotFoundException("BillingPeriod", id);
            }

            if (period.Status != BillingPeriodStatus.Closed)
            {
                throw new AppException("Settlement PDFs are only available for closed billing periods.");
            }

            var blobPath = $"{id}/{houseId}.pdf";

            // Check if PDF already exists in Blob Storage (cached)
            var pdfBytes = await GetOrGenerateSettlementPdfAsync(period, houseId, blobPath);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/pdf");
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"vyuctovani-{houseId}.pdf\"");
            await response.Body.WriteAsync(pdfBytes);
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

    [Function("GetAllSettlementsPdf")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> GetAllSettlementsPdfAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "billing-periods/{id}/pdf")] HttpRequestData req,
        FunctionContext context,
        string id)
    {
        try
        {
            if (!Guid.TryParse(id, out _))
                return await WriteErrorResponseAsync(req, 400, "Invalid ID format.");

            var user = GetAuthenticatedUser(context);

            // Check if period exists and is closed
            var period = await _billingPeriodRepository.GetAsync(PartitionKeys.Period, id);
            if (period is null)
            {
                throw new NotFoundException("BillingPeriod", id);
            }

            if (period.Status != BillingPeriodStatus.Closed)
            {
                throw new AppException("Settlement PDFs are only available for closed billing periods.");
            }

            // Get all settlements for this period
            var settlements = await _settlementRepository.GetByPeriodIdAsync(id);
            if (settlements.Count == 0)
            {
                throw new AppException("No settlements found for this billing period.");
            }

            // Generate ZIP with all PDFs
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
                var houseMap = houses.ToDictionary(h => h.Id);

                foreach (var settlement in settlements)
                {
                    var blobPath = $"{id}/{settlement.HouseId}.pdf";
                    var pdfBytes = await GetOrGenerateSettlementPdfAsync(period, settlement.HouseId, blobPath);

                    var houseName = houseMap.TryGetValue(settlement.HouseId, out var house)
                        ? house.Name
                        : settlement.HouseId;

                    // Sanitize house name for filename
                    var safeHouseName = string.Join("_", houseName.Split(Path.GetInvalidFileNameChars()));
                    var entry = archive.CreateEntry($"vyuctovani-{safeHouseName}.pdf", CompressionLevel.Fastest);

                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(pdfBytes);
                }
            }

            zipStream.Position = 0;

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/zip");
            var safePeriodName = string.Join("_", period.Name.Split(Path.GetInvalidFileNameChars()));
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"vyuctovani-{safePeriodName}.zip\"");
            await zipStream.CopyToAsync(response.Body);
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
    /// Retrieves a cached settlement PDF from Blob Storage, or generates and caches a new one.
    /// </summary>
    private async Task<byte[]> GetOrGenerateSettlementPdfAsync(
        BillingPeriod period, string houseId, string blobPath)
    {
        // Check cache first
        if (await _blobStorageService.ExistsAsync(BlobContainerNames.Settlements, blobPath))
        {
            _logger.LogInformation("Settlement PDF found in cache: {BlobPath}.", blobPath);
            var stream = await _blobStorageService.DownloadAsync(BlobContainerNames.Settlements, blobPath);
            if (stream is not null)
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                return ms.ToArray();
            }
        }

        // Generate the PDF
        var house = await _houseRepository.GetAsync(PartitionKeys.House, houseId);
        if (house is null)
        {
            throw new NotFoundException("House", houseId);
        }

        var settlements = await _settlementRepository.GetByPeriodIdAsync(period.Id);
        var settlement = settlements.FirstOrDefault(s => s.HouseId == houseId);
        if (settlement is null)
        {
            throw new NotFoundException("Settlement", $"{period.Id}/{houseId}");
        }

        // Compute totals for the PDF
        var invoices = await _invoiceRepository.GetByPeriodAsync(period.DateFrom, period.DateTo);
        var totalInvoiceAmount = invoices.Sum(i => i.Amount);
        var totalNetworkConsumption = settlements.Sum(s => s.ConsumptionM3 + s.LossAllocatedM3);

        var detail = new HouseSettlementDetail(
            HouseId: settlement.HouseId,
            HouseName: house.Name,
            ConsumptionM3: settlement.ConsumptionM3,
            LossAllocatedM3: settlement.LossAllocatedM3,
            SharePercent: settlement.SharePercent,
            CalculatedAmount: settlement.CalculatedAmount,
            TotalAdvances: settlement.TotalAdvances,
            Balance: settlement.Balance
        );

        var pdfBytes = _generateSettlementPdfUseCase.Generate(
            period, house, detail, totalNetworkConsumption, totalInvoiceAmount);

        // Cache the generated PDF in Blob Storage
        await _blobStorageService.UploadAsync(
            BlobContainerNames.Settlements, blobPath, pdfBytes, "application/pdf");

        _logger.LogInformation(
            "Generated and cached settlement PDF for house {HouseId}, period {PeriodId} ({Size} bytes).",
            houseId, period.Id, pdfBytes.Length);

        return pdfBytes;
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
