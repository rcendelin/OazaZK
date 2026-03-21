using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Application.Validators;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class ReadingFunctions
{
    private readonly ImportReadingsUseCase _importUseCase;
    private readonly IMeterReadingRepository _readingRepository;
    private readonly IWaterMeterRepository _meterRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ILogger<ReadingFunctions> _logger;

    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ReadingFunctions(
        ImportReadingsUseCase importUseCase,
        IMeterReadingRepository readingRepository,
        IWaterMeterRepository meterRepository,
        IHouseRepository houseRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ILogger<ReadingFunctions> logger)
    {
        _importUseCase = importUseCase ?? throw new ArgumentNullException(nameof(importUseCase));
        _readingRepository = readingRepository ?? throw new ArgumentNullException(nameof(readingRepository));
        _meterRepository = meterRepository ?? throw new ArgumentNullException(nameof(meterRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("ImportReadings")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> ImportReadingsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "readings/import")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            // Read the multipart form data to extract the file
            var fileStream = await ExtractFileFromRequestAsync(req);

            var result = await _importUseCase.ParseAndValidateAsync(fileStream, user.Id);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, result);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during readings import.");
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred during import.");
        }
    }

    [Function("ConfirmImport")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> ConfirmImportAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "readings/import/confirm")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var request = await JsonSerializer.DeserializeAsync<ConfirmImportRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new ConfirmImportRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var count = await _importUseCase.ConfirmImportAsync(request.ImportSessionId, user.Id);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, new { count, message = $"Successfully imported {count} readings." });
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during import confirmation.");
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("GetReadings")]
    public async Task<HttpResponseData> GetReadingsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "readings")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            if (!int.TryParse(query["year"], out var year) || !int.TryParse(query["month"], out var month))
            {
                return await WriteErrorResponseAsync(req, 400, "Query parameters 'year' and 'month' are required and must be integers.");
            }

            if (year < 2000 || year > 2100 || month < 1 || month > 12)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid year or month value.");
            }

            // Load all meters and houses for enrichment
            var allMeters = await _meterRepository.GetByPartitionKeyAsync(PartitionKeys.Meter);
            var allHouses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);

            var houseLookup = allHouses.ToDictionary(h => h.Id, h => h.Name);
            var meterLookup = allMeters.ToDictionary(m => m.Id, m => m);

            // Determine which meters to query based on user role
            IEnumerable<WaterMeter> metersToQuery;
            if (user.Role == UserRole.Admin)
            {
                metersToQuery = allMeters;
            }
            else
            {
                // Member: only their house's meter(s)
                if (string.IsNullOrEmpty(user.HouseId))
                {
                    return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                        new MonthlyReadingsResponse { Year = year, Month = month });
                }
                metersToQuery = allMeters.Where(m => m.HouseId == user.HouseId);
            }

            var readings = new List<ReadingResponse>();

            foreach (var meter in metersToQuery)
            {
                // Get all readings for this meter, then filter by year/month
                var meterReadings = await _readingRepository.GetByMeterIdAsync(meter.Id);

                var monthReadings = meterReadings
                    .Where(r => r.ReadingDate.Year == year && r.ReadingDate.Month == month)
                    .ToList();

                foreach (var reading in monthReadings)
                {
                    // Calculate consumption from previous reading
                    decimal? consumption = null;
                    var previousReading = meterReadings
                        .Where(r => r.ReadingDate < reading.ReadingDate)
                        .OrderByDescending(r => r.ReadingDate)
                        .FirstOrDefault();

                    if (previousReading is not null)
                    {
                        consumption = reading.Value - previousReading.Value;
                    }

                    string? houseName = null;
                    if (meter.HouseId is not null && houseLookup.TryGetValue(meter.HouseId, out var name))
                    {
                        houseName = name;
                    }

                    readings.Add(new ReadingResponse
                    {
                        MeterId = meter.Id,
                        MeterNumber = meter.MeterNumber,
                        HouseName = houseName,
                        ReadingDate = reading.ReadingDate,
                        Value = reading.Value,
                        Consumption = consumption,
                        Source = reading.Source.ToString(),
                        ImportedAt = reading.ImportedAt,
                        ImportedBy = reading.ImportedBy
                    });
                }
            }

            var response = new MonthlyReadingsResponse
            {
                Year = year,
                Month = month,
                Readings = readings.OrderBy(r => r.MeterNumber).ToList()
            };

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, response);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting readings.");
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("CreateReading")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateReadingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "readings")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var request = await JsonSerializer.DeserializeAsync<CreateReadingRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new CreateReadingRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            // Verify meter exists
            var meter = await _meterRepository.GetAsync(PartitionKeys.Meter, request.MeterId);
            if (meter is null)
            {
                throw new NotFoundException("WaterMeter", request.MeterId);
            }

            var readingDate = DateTime.SpecifyKind(request.ReadingDate.Date, DateTimeKind.Utc);

            // Check for duplicate (same meter + same month)
            var existingReadings = await _readingRepository.GetByMeterIdAsync(request.MeterId);
            var duplicate = existingReadings.FirstOrDefault(r =>
                r.ReadingDate.Year == readingDate.Year && r.ReadingDate.Month == readingDate.Month);
            if (duplicate is not null)
            {
                throw new AppException(
                    $"A reading already exists for meter '{meter.MeterNumber}' in {readingDate:yyyy-MM}.");
            }

            // Check for negative consumption
            var previousReading = existingReadings
                .Where(r => r.ReadingDate < readingDate)
                .OrderByDescending(r => r.ReadingDate)
                .FirstOrDefault();

            if (previousReading is not null && request.Value < previousReading.Value)
            {
                throw new AppException(
                    $"Negative consumption: new value {request.Value} is less than previous value {previousReading.Value}.");
            }

            var reading = new MeterReading
            {
                MeterId = request.MeterId,
                ReadingDate = readingDate,
                Value = request.Value,
                Source = ReadingSource.Manual,
                ImportedAt = DateTime.UtcNow,
                ImportedBy = user.Id
            };

            await _readingRepository.UpsertAsync(reading);

            _logger.LogInformation("Manual reading created for meter {MeterId} on {Date}.", request.MeterId, readingDate);

            // Build response with enrichment
            string? houseName = null;
            if (meter.HouseId is not null)
            {
                var house = await _houseRepository.GetAsync(PartitionKeys.House, meter.HouseId);
                houseName = house?.Name;
            }

            decimal? consumption = previousReading is not null ? request.Value - previousReading.Value : null;

            var readingResponse = new ReadingResponse
            {
                MeterId = reading.MeterId,
                MeterNumber = meter.MeterNumber,
                HouseName = houseName,
                ReadingDate = reading.ReadingDate,
                Value = reading.Value,
                Consumption = consumption,
                Source = reading.Source.ToString(),
                ImportedAt = reading.ImportedAt,
                ImportedBy = reading.ImportedBy
            };

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created, readingResponse);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating reading.");
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("UpdateReading")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateReadingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "readings/{meterId}/{date}")] HttpRequestData req,
        string meterId,
        string date,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var readingDate))
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid date format. Use YYYY-MM-DD.");
            }

            readingDate = DateTime.SpecifyKind(readingDate, DateTimeKind.Utc);

            var request = await JsonSerializer.DeserializeAsync<UpdateReadingRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new UpdateReadingRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            // Verify meter exists
            var meter = await _meterRepository.GetAsync(PartitionKeys.Meter, meterId);
            if (meter is null)
            {
                throw new NotFoundException("WaterMeter", meterId);
            }

            // Find the existing reading by RowKey (inverted timestamp)
            var rowKey = InvertedTimestamp.FromDateTime(readingDate);
            var existing = await _readingRepository.GetAsync(meterId, rowKey);
            if (existing is null)
            {
                throw new NotFoundException("MeterReading", $"{meterId}/{date}");
            }

            // Check if reading falls in a closed billing period
            var allPeriods = await _billingPeriodRepository.GetByPartitionKeyAsync(PartitionKeys.Period);
            var inClosedPeriod = allPeriods.Any(p =>
                p.Status == BillingPeriodStatus.Closed &&
                readingDate >= p.DateFrom && readingDate <= p.DateTo);
            if (inClosedPeriod)
            {
                return await WriteErrorResponseAsync(req, 409, "Cannot modify a reading in a closed billing period.");
            }

            // Check for negative consumption after correction
            var existingReadings = await _readingRepository.GetByMeterIdAsync(meterId);
            var previousReading = existingReadings
                .Where(r => r.ReadingDate < readingDate)
                .OrderByDescending(r => r.ReadingDate)
                .FirstOrDefault();

            if (previousReading is not null && request.Value < previousReading.Value)
            {
                throw new AppException(
                    $"Negative consumption: new value {request.Value} is less than previous value {previousReading.Value}.");
            }

            // Also check that the next reading is not less than the new value
            var nextReading = existingReadings
                .Where(r => r.ReadingDate > readingDate)
                .OrderBy(r => r.ReadingDate)
                .FirstOrDefault();

            if (nextReading is not null && nextReading.Value < request.Value)
            {
                throw new AppException(
                    $"Negative consumption: next reading value {nextReading.Value} would be less than corrected value {request.Value}.");
            }

            existing.Value = request.Value;
            existing.ImportedAt = DateTime.UtcNow;
            existing.ImportedBy = user.Id;

            await _readingRepository.UpsertAsync(existing);

            _logger.LogInformation("Reading updated for meter {MeterId} on {Date}. New value: {Value}.",
                meterId, date, request.Value);

            // Build response
            string? houseName = null;
            if (meter.HouseId is not null)
            {
                var house = await _houseRepository.GetAsync(PartitionKeys.House, meter.HouseId);
                houseName = house?.Name;
            }

            decimal? consumption = previousReading is not null ? request.Value - previousReading.Value : null;

            var readingResponse = new ReadingResponse
            {
                MeterId = existing.MeterId,
                MeterNumber = meter.MeterNumber,
                HouseName = houseName,
                ReadingDate = existing.ReadingDate,
                Value = existing.Value,
                Consumption = consumption,
                Source = existing.Source.ToString(),
                ImportedAt = existing.ImportedAt,
                ImportedBy = existing.ImportedBy
            };

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, readingResponse);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating reading.");
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    private static readonly string[] CzechMonthNames =
    {
        "leden", "únor", "březen", "duben", "květen", "červen",
        "červenec", "srpen", "září", "říjen", "listopad", "prosinec"
    };

    [Function("GetChartData")]
    public async Task<HttpResponseData> GetChartDataAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "readings/chart")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var houseIdParam = query["houseId"];
            var fromParam = query["from"];
            var toParam = query["to"];

            // Determine date range (default: last 12 months)
            DateTime dateTo;
            DateTime dateFrom;

            if (!string.IsNullOrEmpty(toParam) && DateTime.TryParse(toParam, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
            {
                dateTo = DateTime.SpecifyKind(parsedTo, DateTimeKind.Utc);
            }
            else
            {
                dateTo = DateTime.UtcNow;
            }

            if (!string.IsNullOrEmpty(fromParam) && DateTime.TryParse(fromParam, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
            {
                dateFrom = DateTime.SpecifyKind(parsedFrom, DateTimeKind.Utc);
            }
            else
            {
                dateFrom = dateTo.AddMonths(-12);
            }

            // Determine which house to query
            string? effectiveHouseId = null;
            string? houseName = null;

            if (user.Role == UserRole.Member)
            {
                // Member can only see their own house's data
                if (!string.IsNullOrEmpty(houseIdParam) && houseIdParam != user.HouseId)
                    return await WriteErrorResponseAsync(req, 403, "Access denied.");
                effectiveHouseId = user.HouseId;
                if (!string.IsNullOrEmpty(effectiveHouseId))
                {
                    var memberHouse = await _houseRepository.GetAsync(PartitionKeys.House, effectiveHouseId);
                    houseName = memberHouse?.Name;
                }
            }
            else if (!string.IsNullOrEmpty(houseIdParam))
            {
                effectiveHouseId = houseIdParam;
                var house = await _houseRepository.GetAsync(PartitionKeys.House, houseIdParam);
                houseName = house?.Name;
            }
            else if (user.Role != UserRole.Admin)
            {
                // Member: use their own house
                effectiveHouseId = user.HouseId;
                if (!string.IsNullOrEmpty(effectiveHouseId))
                {
                    var house = await _houseRepository.GetAsync(PartitionKeys.House, effectiveHouseId);
                    houseName = house?.Name;
                }
            }
            // If no houseId and admin: aggregate all houses

            var allMeters = await _meterRepository.GetByPartitionKeyAsync(PartitionKeys.Meter);

            IEnumerable<WaterMeter> metersToQuery;
            if (!string.IsNullOrEmpty(effectiveHouseId))
            {
                metersToQuery = allMeters.Where(m => m.HouseId == effectiveHouseId);
            }
            else
            {
                // Admin: all individual meters (not main)
                metersToQuery = allMeters.Where(m => m.Type == MeterType.Individual);
            }

            // Collect all readings for the relevant meters
            var allReadings = new List<(string MeterId, MeterReading Reading)>();
            foreach (var meter in metersToQuery)
            {
                var readings = await _readingRepository.GetByMeterIdAsync(meter.Id);
                foreach (var r in readings)
                {
                    allReadings.Add((meter.Id, r));
                }
            }

            // Group by meter, then compute monthly consumption
            var monthlyConsumption = new Dictionary<(int Year, int Month), decimal>();

            var readingsByMeter = allReadings.GroupBy(r => r.MeterId);
            foreach (var group in readingsByMeter)
            {
                var sorted = group.Select(g => g.Reading).OrderBy(r => r.ReadingDate).ToList();
                for (var i = 1; i < sorted.Count; i++)
                {
                    var current = sorted[i];
                    var previous = sorted[i - 1];
                    var consumption = current.Value - previous.Value;
                    if (consumption < 0) continue; // Skip negative

                    var key = (current.ReadingDate.Year, current.ReadingDate.Month);

                    // Check if within date range
                    if (current.ReadingDate < dateFrom || current.ReadingDate > dateTo) continue;

                    if (monthlyConsumption.ContainsKey(key))
                    {
                        monthlyConsumption[key] += consumption;
                    }
                    else
                    {
                        monthlyConsumption[key] = consumption;
                    }
                }
            }

            // Build sorted data points
            var dataPoints = monthlyConsumption
                .OrderBy(kv => kv.Key.Year)
                .ThenBy(kv => kv.Key.Month)
                .Select(kv => new ChartDataPoint(
                    kv.Key.Year,
                    kv.Key.Month,
                    $"{CzechMonthNames[kv.Key.Month - 1]} {kv.Key.Year}",
                    kv.Value))
                .ToList();

            var response = new ChartResponse(effectiveHouseId, houseName, dataPoints);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, response);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting chart data.");
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) &&
            userObj is User user)
        {
            return user;
        }

        throw new AppException("Unauthorized.", 401);
    }

    private static async Task<Stream> ExtractFileFromRequestAsync(HttpRequestData req)
    {
        // Check content type
        if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
        {
            throw new AppException("Content-Type header is required.");
        }

        var contentType = contentTypeValues.FirstOrDefault() ?? string.Empty;

        // Handle both multipart form data and direct file upload
        if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            // Extract boundary from content type
            var boundaryIndex = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
            if (boundaryIndex < 0)
            {
                throw new AppException("Multipart boundary not found in Content-Type header.");
            }

            var boundary = contentType[(boundaryIndex + "boundary=".Length)..].Trim().Trim('"');
            var (fileStream, _) = await ExtractFileFromMultipartAsync(req, boundary);
            return fileStream;
        }

        // Direct file upload (application/octet-stream or xlsx content type)
        if (contentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            var bodyBytes = await ReadBodyBytesWithLimitAsync(req.Body);
            return new MemoryStream(bodyBytes);
        }

        throw new AppException("Unsupported content type. Use multipart/form-data or application/octet-stream with .xlsx file.");
    }

    private static async Task<(Stream fileStream, string? fileName)> ExtractFileFromMultipartAsync(
        HttpRequestData req, string boundary)
    {
        var bodyBytes = await ReadBodyBytesWithLimitAsync(req.Body);
        var boundaryBytes = Encoding.ASCII.GetBytes("--" + boundary);
        var crlfCrlf = Encoding.ASCII.GetBytes("\r\n\r\n");

        // Find parts by searching for boundary bytes
        var partStarts = FindAllOccurrences(bodyBytes, boundaryBytes);

        foreach (var partStart in partStarts)
        {
            var headerEnd = FindOccurrence(bodyBytes, crlfCrlf, partStart + boundaryBytes.Length);
            if (headerEnd < 0) continue;

            var headerStartOffset = partStart + boundaryBytes.Length + 2; // +2 for CRLF after boundary
            if (headerStartOffset > headerEnd) continue;

            var headerLength = headerEnd - headerStartOffset;
            var headerBytes = new byte[headerLength];
            Array.Copy(bodyBytes, headerStartOffset, headerBytes, 0, headerBytes.Length);
            var headerText = Encoding.ASCII.GetString(headerBytes);

            if (!headerText.Contains("filename=", StringComparison.OrdinalIgnoreCase)) continue;

            // Extract filename
            var filenameMatch = Regex.Match(headerText, @"filename=""?([^"";\r\n]+)""?");
            var fileName = filenameMatch.Success ? filenameMatch.Groups[1].Value.Trim() : null;

            // Find next boundary or end
            var contentStart = headerEnd + crlfCrlf.Length;
            var nextBoundary = FindOccurrence(bodyBytes, boundaryBytes, contentStart);
            var contentEnd = nextBoundary > 0 ? nextBoundary - 2 : bodyBytes.Length; // -2 for CRLF before boundary

            if (contentEnd < contentStart)
            {
                contentEnd = contentStart;
            }

            var contentLength = contentEnd - contentStart;
            var ms = new MemoryStream(bodyBytes, contentStart, contentLength);
            return (ms, fileName);
        }

        throw new AppException("No file found in multipart request.", 400);
    }

    private static async Task<byte[]> ReadBodyBytesWithLimitAsync(Stream body, long maxBytes = MaxFileSizeBytes)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await body.ReadAsync(buffer)) > 0)
        {
            totalRead += bytesRead;
            if (totalRead > maxBytes)
                throw new AppException($"File exceeds maximum size of {maxBytes / (1024 * 1024)} MB.", 400);
            ms.Write(buffer, 0, bytesRead);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Finds the first occurrence of needle in haystack starting at startIndex.
    /// </summary>
    private static int FindOccurrence(byte[] haystack, byte[] needle, int startIndex = 0)
    {
        for (var i = startIndex; i <= haystack.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    found = false;
                    break;
                }
            }
            if (found) return i;
        }
        return -1;
    }

    /// <summary>
    /// Finds all occurrences of needle in haystack.
    /// </summary>
    private static List<int> FindAllOccurrences(byte[] haystack, byte[] needle)
    {
        var results = new List<int>();
        var index = 0;
        while (index <= haystack.Length - needle.Length)
        {
            var found = FindOccurrence(haystack, needle, index);
            if (found < 0) break;
            results.Add(found);
            index = found + needle.Length;
        }
        return results;
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
