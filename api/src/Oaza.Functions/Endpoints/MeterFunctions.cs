using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
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

public class MeterFunctions
{
    private readonly IWaterMeterRepository _meterRepository;
    private readonly ILogger<MeterFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public MeterFunctions(IWaterMeterRepository meterRepository, ILogger<MeterFunctions> logger)
    {
        _meterRepository = meterRepository ?? throw new ArgumentNullException(nameof(meterRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetMeters")]
    public async Task<HttpResponseData> GetMetersAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "meters")] HttpRequestData req)
    {
        try
        {
            var meters = await _meterRepository.GetByPartitionKeyAsync(PartitionKeys.Meter);
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                meters.Select(EntityMapper.ToResponse).ToList());
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

    [Function("CreateMeter")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateMeterAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "meters")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateMeterRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new CreateMeterRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            if (!Enum.TryParse<MeterType>(request.Type, ignoreCase: true, out var meterType))
            {
                return await WriteErrorResponseAsync(req, 400, $"Invalid meter type: {request.Type}");
            }

            var meter = new WaterMeter
            {
                Id = Guid.NewGuid().ToString(),
                MeterNumber = request.MeterNumber,
                Type = meterType,
                HouseId = request.HouseId,
                InstallationDate = DateTime.UtcNow,
            };

            await _meterRepository.UpsertAsync(meter);

            _logger.LogInformation("Meter {MeterId} created: {MeterNumber}.", meter.Id, meter.MeterNumber);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(meter));
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

    [Function("UpdateMeter")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateMeterAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "meters/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await _meterRepository.GetAsync(PartitionKeys.Meter, id);
            if (existing is null)
            {
                throw new NotFoundException("Meter", id);
            }

            var request = await JsonSerializer.DeserializeAsync<UpdateMeterRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new UpdateMeterRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            existing.MeterNumber = request.MeterNumber;
            existing.HouseId = request.HouseId;

            await _meterRepository.UpsertAsync(existing);

            _logger.LogInformation("Meter {MeterId} updated.", id);

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
