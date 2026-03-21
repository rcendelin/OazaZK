using System.Net;
using System.Text.Json;
using FluentValidation;
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

public class HouseFunctions
{
    private readonly IHouseRepository _houseRepository;
    private readonly ILogger<HouseFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public HouseFunctions(IHouseRepository houseRepository, ILogger<HouseFunctions> logger)
    {
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetHouses")]
    public async Task<HttpResponseData> GetHousesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "houses")] HttpRequestData req)
    {
        try
        {
            var houses = await _houseRepository.GetByPartitionKeyAsync(PartitionKeys.House);
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                houses.Select(EntityMapper.ToResponse).ToList());
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
    }

    [Function("GetHouseById")]
    public async Task<HttpResponseData> GetHouseByIdAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "houses/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var house = await _houseRepository.GetAsync(PartitionKeys.House, id);
            if (house is null)
            {
                throw new NotFoundException("House", id);
            }

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                EntityMapper.ToResponse(house));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
    }

    [Function("CreateHouse")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateHouseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "houses")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateHouseRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new CreateHouseRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var house = new House
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                Address = request.Address,
                ContactPerson = request.ContactPerson,
                Email = request.Email,
                IsActive = true,
            };

            await _houseRepository.UpsertAsync(house);

            _logger.LogInformation("House {HouseId} created: {Name}.", house.Id, house.Name);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(house));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
    }

    [Function("UpdateHouse")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateHouseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "houses/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await _houseRepository.GetAsync(PartitionKeys.House, id);
            if (existing is null)
            {
                throw new NotFoundException("House", id);
            }

            var request = await JsonSerializer.DeserializeAsync<UpdateHouseRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new UpdateHouseRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            existing.Name = request.Name;
            existing.Address = request.Address;
            existing.ContactPerson = request.ContactPerson;
            existing.Email = request.Email;
            existing.IsActive = request.IsActive;

            await _houseRepository.UpsertAsync(existing);

            _logger.LogInformation("House {HouseId} updated.", id);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                EntityMapper.ToResponse(existing));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
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
