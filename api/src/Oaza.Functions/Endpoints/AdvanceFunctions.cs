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

public class AdvanceFunctions
{
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IBillingPeriodRepository _billingPeriodRepository;
    private readonly ILogger<AdvanceFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AdvanceFunctions(
        IAdvancePaymentRepository advanceRepository,
        IHouseRepository houseRepository,
        IBillingPeriodRepository billingPeriodRepository,
        ILogger<AdvanceFunctions> logger)
    {
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _billingPeriodRepository = billingPeriodRepository ?? throw new ArgumentNullException(nameof(billingPeriodRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetAdvances")]
    public async Task<HttpResponseData> GetAdvancesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "advances")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var houseIdParam = queryParams["houseId"];
            var yearParam = queryParams["year"];

            // Members can only see their own house's advances
            if (user.Role == UserRole.Member)
            {
                if (string.IsNullOrEmpty(user.HouseId))
                {
                    return await WriteJsonResponseAsync(req, HttpStatusCode.OK, Array.Empty<AdvanceResponse>());
                }

                if (!string.IsNullOrEmpty(houseIdParam) && houseIdParam != user.HouseId)
                {
                    return await WriteErrorResponseAsync(req, 403, "Access denied.");
                }

                houseIdParam = user.HouseId;
            }

            // Build house name lookup
            var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
            var houseNameMap = houses.ToDictionary(h => h.Id, h => h.Name);

            IReadOnlyList<AdvancePayment> advances;
            if (!string.IsNullOrEmpty(houseIdParam))
            {
                advances = await _advanceRepository.GetByHouseIdAsync(houseIdParam);
            }
            else
            {
                // Admin/Accountant: get all advances by querying each house
                var allAdvances = new List<AdvancePayment>();
                foreach (var house in houses)
                {
                    var houseAdvances = await _advanceRepository.GetByHouseIdAsync(house.Id);
                    allAdvances.AddRange(houseAdvances);
                }
                advances = allAdvances.AsReadOnly();
            }

            // Filter by year if specified
            if (int.TryParse(yearParam, out var year))
            {
                advances = advances.Where(a => a.Year == year).ToList().AsReadOnly();
            }

            var responses = advances
                .Select(a => EntityMapper.ToResponse(a, houseNameMap.GetValueOrDefault(a.HouseId)))
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

    [Function("CreateAdvance")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateAdvanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "advances")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateAdvanceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new CreateAdvanceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            // Verify house exists
            var house = await _houseRepository.GetAsync(PartitionKeys.House, request.HouseId);
            if (house is null)
            {
                return await WriteErrorResponseAsync(req, 404, $"House '{request.HouseId}' not found.");
            }

            // Check for duplicate (same house, same year-month)
            var rowKey = $"{request.Year:D4}-{request.Month:D2}";
            var existing = await _advanceRepository.GetAsync(request.HouseId, rowKey);
            if (existing is not null)
            {
                return await WriteErrorResponseAsync(req, 409,
                    $"Advance payment for house '{house.Name}' for {request.Year}-{request.Month:D2} already exists.");
            }

            var payment = new AdvancePayment
            {
                HouseId = request.HouseId,
                Year = request.Year,
                Month = request.Month,
                Amount = request.Amount,
                PaymentDate = request.PaymentDate,
            };

            await _advanceRepository.UpsertAsync(payment);

            _logger.LogInformation("Advance payment created for house {HouseId} for {Year}-{Month}.",
                payment.HouseId, payment.Year, payment.Month);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(payment, house.Name));
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

    [Function("UpdateAdvance")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateAdvanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "advances/{houseId}/{yearMonth}")] HttpRequestData req,
        string houseId,
        string yearMonth)
    {
        try
        {
            var existing = await _advanceRepository.GetAsync(houseId, yearMonth);
            if (existing is null)
            {
                throw new NotFoundException("AdvancePayment", $"{houseId}/{yearMonth}");
            }

            // Check if advance falls in a closed billing period
            var advanceDate = new DateTime(existing.Year, existing.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var allPeriods = await _billingPeriodRepository.GetByPartitionKeyAsync(PartitionKeys.Period);
            var inClosedPeriod = allPeriods.Any(p =>
                p.Status == BillingPeriodStatus.Closed &&
                advanceDate >= p.DateFrom && advanceDate <= p.DateTo);
            if (inClosedPeriod)
            {
                return await WriteErrorResponseAsync(req, 409, "Cannot modify advance in a closed billing period.");
            }

            var request = await JsonSerializer.DeserializeAsync<UpdateAdvanceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new UpdateAdvanceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            existing.Amount = request.Amount;
            existing.PaymentDate = request.PaymentDate;

            await _advanceRepository.UpsertAsync(existing);

            // Get house name for response
            var house = await _houseRepository.GetAsync(PartitionKeys.House, houseId);
            var houseName = house?.Name;

            _logger.LogInformation("Advance payment updated for house {HouseId} for {YearMonth}.", houseId, yearMonth);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                EntityMapper.ToResponse(existing, houseName));
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
