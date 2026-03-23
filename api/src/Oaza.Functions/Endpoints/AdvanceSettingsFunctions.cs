using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.Exceptions;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Functions.Endpoints;

public class AdvanceSettingsFunctions
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly IHouseRepository _houseRepository;
    private readonly IMeterReadingRepository _readingRepository;
    private readonly IWaterMeterRepository _meterRepository;
    private readonly ILogger<AdvanceSettingsFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AdvanceSettingsFunctions(
        TableServiceClient tableServiceClient,
        IHouseRepository houseRepository,
        IMeterReadingRepository readingRepository,
        IWaterMeterRepository meterRepository,
        ILogger<AdvanceSettingsFunctions> logger)
    {
        _tableServiceClient = tableServiceClient;
        _houseRepository = houseRepository;
        _readingRepository = readingRepository;
        _meterRepository = meterRepository;
        _logger = logger;
    }

    [Function("GetAdvanceSettings")]
    public async Task<HttpResponseData> GetAdvanceSettingsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "advance-settings")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            var settings = await LoadSettingsAsync();
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, settings);
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

    [Function("UpdateAdvanceSettings")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateAdvanceSettingsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "advance-settings")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            var settings = await JsonSerializer.DeserializeAsync<AdvanceSettings>(req.Body, JsonOptions);
            if (settings is null)
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");

            // Validate coefficients sum to ~100%
            if (settings.ElectricityCoefficients.Count > 0)
            {
                var sum = settings.ElectricityCoefficients.Values.Sum();
                if (Math.Abs(sum - 100m) > 0.1m)
                {
                    return await WriteErrorResponseAsync(req, 400,
                        $"Koeficienty elektřiny musí dát dohromady 100%. Aktuální součet: {sum:F1}%.");
                }
            }

            var tableClient = _tableServiceClient.GetTableClient("AdvanceSettings");
            await tableClient.CreateIfNotExistsAsync();

            var entity = TableEntityMapper.ToTableEntity(settings);
            await tableClient.UpsertEntityAsync(entity);

            _logger.LogInformation("Advance settings updated.");

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, settings);
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
    /// Calculates monthly advance per house based on current settings and recent consumption.
    /// </summary>
    [Function("CalculateAdvances")]
    public async Task<HttpResponseData> CalculateAdvancesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "advance-settings/calculate")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            var settings = await LoadSettingsAsync();

            var allMeters = await _meterRepository.GetByPartitionKeyAsync("METER");
            var allHouses = await _houseRepository.GetByPartitionKeyAsync("HOUSE");
            var activeHouses = allHouses.Where(h => h.IsActive).ToList();

            var mainMeter = allMeters.FirstOrDefault(m => m.Type == MeterType.Main);

            // Get last 3 months average consumption per house
            var houseConsumptions = new Dictionary<string, decimal>();
            decimal totalConsumption = 0;

            foreach (var house in activeHouses)
            {
                var meter = allMeters.FirstOrDefault(m => m.HouseId == house.Id);
                if (meter == null) { houseConsumptions[house.Id] = 0; continue; }

                var readings = await _readingRepository.GetByMeterIdAsync(meter.Id);
                var sorted = readings.OrderByDescending(r => r.ReadingDate).Take(4).OrderBy(r => r.ReadingDate).ToList();

                decimal avgMonthly = 0;
                if (sorted.Count >= 2)
                {
                    var totalDelta = sorted.Last().Value - sorted.First().Value;
                    var months = Math.Max(1, (sorted.Last().ReadingDate - sorted.First().ReadingDate).TotalDays / 30.0);
                    avgMonthly = totalDelta / (decimal)months;
                }

                houseConsumptions[house.Id] = Math.Max(0, avgMonthly);
                totalConsumption += Math.Max(0, avgMonthly);
            }

            // Calculate main meter average monthly consumption for loss
            decimal mainMonthlyConsumption = 0;
            if (mainMeter != null)
            {
                var mainReadings = await _readingRepository.GetByMeterIdAsync(mainMeter.Id);
                var sorted = mainReadings.OrderByDescending(r => r.ReadingDate).Take(4).OrderBy(r => r.ReadingDate).ToList();
                if (sorted.Count >= 2)
                {
                    var totalDelta = sorted.Last().Value - sorted.First().Value;
                    var months = Math.Max(1, (sorted.Last().ReadingDate - sorted.First().ReadingDate).TotalDays / 30.0);
                    mainMonthlyConsumption = totalDelta / (decimal)months;
                }
            }

            var monthlyLoss = Math.Max(0, mainMonthlyConsumption - totalConsumption);

            // Build result per house
            var result = new List<object>();

            foreach (var house in activeHouses)
            {
                var consumption = houseConsumptions.GetValueOrDefault(house.Id, 0);
                var sharePercent = totalConsumption > 0 ? consumption / totalConsumption * 100m : (100m / activeHouses.Count);

                // Loss allocation (proportional)
                var lossShare = totalConsumption > 0
                    ? monthlyLoss * (consumption / totalConsumption)
                    : monthlyLoss / activeHouses.Count;

                var totalWaterM3 = consumption + lossShare;
                var waterCost = totalWaterM3 * settings.WaterPricePerM3;

                // Electricity share
                var elecCoeff = settings.ElectricityCoefficients.GetValueOrDefault(house.Id, 0);
                var electricityCost = settings.MonthlyElectricityCost * elecCoeff / 100m;

                var totalAdvance = settings.MonthlyAssociationFee + electricityCost + waterCost;

                result.Add(new
                {
                    houseId = house.Id,
                    houseName = house.Name,
                    avgMonthlyConsumptionM3 = Math.Round(consumption, 2),
                    lossShareM3 = Math.Round(lossShare, 2),
                    totalWaterM3 = Math.Round(totalWaterM3, 2),
                    sharePercent = Math.Round(sharePercent, 1),
                    waterCostCzk = Math.Round(waterCost, 2),
                    associationFeeCzk = settings.MonthlyAssociationFee,
                    electricityCoefficient = elecCoeff,
                    electricityCostCzk = Math.Round(electricityCost, 2),
                    totalAdvanceCzk = Math.Round(totalAdvance, 2),
                });
            }

            var summary = new
            {
                settings = new
                {
                    settings.WaterPricePerM3,
                    settings.WaterPriceValidFrom,
                    settings.WaterPriceValidTo,
                    settings.MonthlyAssociationFee,
                    settings.MonthlyElectricityCost,
                    settings.LossAllocationMethod,
                },
                mainMeterMonthlyConsumptionM3 = Math.Round(mainMonthlyConsumption, 2),
                totalIndividualMonthlyM3 = Math.Round(totalConsumption, 2),
                monthlyLossM3 = Math.Round(monthlyLoss, 2),
                houses = result,
            };

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

    private async Task<AdvanceSettings> LoadSettingsAsync()
    {
        var tableClient = _tableServiceClient.GetTableClient("AdvanceSettings");
        await tableClient.CreateIfNotExistsAsync();

        try
        {
            var response = await tableClient.GetEntityAsync<TableEntity>("SETTINGS", "advances");
            return TableEntityMapper.ToAdvanceSettings(response.Value);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return new AdvanceSettings();
        }
    }

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) && userObj is User user)
            return user;
        throw new AppException("User not authenticated.", 401);
    }

    private static async Task<HttpResponseData> WriteJsonResponseAsync<T>(HttpRequestData req, HttpStatusCode status, T data)
    {
        var response = req.CreateResponse(status);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(json);
        return response;
    }

    private static async Task<HttpResponseData> WriteErrorResponseAsync(HttpRequestData req, int statusCode, string message)
    {
        var response = req.CreateResponse((HttpStatusCode)statusCode);
        await response.WriteAsJsonAsync(new { error = message });
        return response;
    }
}
