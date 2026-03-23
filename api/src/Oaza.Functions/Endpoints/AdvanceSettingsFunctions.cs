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
            GetAuthenticatedUser(context);
            var settings = await LoadSettingsAsync();
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, settings);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Advance settings error.");
            return await WriteErrorResponseAsync(req, 500, $"Chyba: {ex.GetType().Name}: {ex.Message}");
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
            GetAuthenticatedUser(context);
            var settings = await JsonSerializer.DeserializeAsync<AdvanceSettings>(req.Body, JsonOptions);
            if (settings is null)
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");

            // Ensure collections are never null
            settings.ElectricityCoefficients ??= new Dictionary<string, decimal>();
            settings.HouseOverrides ??= new Dictionary<string, HouseAdvanceOverride>();
            settings.LossAllocationMethod ??= "ProportionalToConsumption";

            if (settings.ElectricityCoefficients.Count > 0)
            {
                var sum = settings.ElectricityCoefficients.Values.Sum();
                if (Math.Abs(sum - 100m) > 0.1m)
                    return await WriteErrorResponseAsync(req, 400,
                        $"Koeficienty elektřiny musí dát dohromady 100%. Aktuální součet: {sum:F1}%.");
            }

            var tableClient = _tableServiceClient.GetTableClient("AdvanceSettings");
            await tableClient.CreateIfNotExistsAsync();
            await tableClient.UpsertEntityAsync(TableEntityMapper.ToTableEntity(settings));

            _logger.LogInformation("Advance settings updated.");
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, settings);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating advance settings.");
            return await WriteErrorResponseAsync(req, 500, $"Chyba při ukládání: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [Function("CalculateAdvances")]
    public async Task<HttpResponseData> CalculateAdvancesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "advance-settings/calculate")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            GetAuthenticatedUser(context);
            var settings = await LoadSettingsAsync();

            var allMeters = await _meterRepository.GetByPartitionKeyAsync("METER");
            var allHouses = await _houseRepository.GetByPartitionKeyAsync("HOUSE");
            var activeHouses = allHouses.Where(h => h.IsActive).ToList();
            var mainMeter = allMeters.FirstOrDefault(m => m.Type == MeterType.Main);

            // Compute average monthly consumption per house (last 3 reading intervals)
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

            // Main meter average for loss calculation
            decimal mainMonthly = 0;
            if (mainMeter != null)
            {
                var mr = await _readingRepository.GetByMeterIdAsync(mainMeter.Id);
                var sorted = mr.OrderByDescending(r => r.ReadingDate).Take(4).OrderBy(r => r.ReadingDate).ToList();
                if (sorted.Count >= 2)
                {
                    var d = sorted.Last().Value - sorted.First().Value;
                    var m = Math.Max(1, (sorted.Last().ReadingDate - sorted.First().ReadingDate).TotalDays / 30.0);
                    mainMonthly = d / (decimal)m;
                }
            }

            var monthlyLoss = Math.Max(0, mainMonthly - totalConsumption);

            // Build per-house result
            var houses = new List<object>();
            foreach (var house in activeHouses)
            {
                var consumption = houseConsumptions.GetValueOrDefault(house.Id, 0);
                var share = totalConsumption > 0 ? consumption / totalConsumption : 1m / activeHouses.Count;

                // Loss distributed equally across all active houses
                var lossShare = activeHouses.Count > 0
                    ? monthlyLoss / activeHouses.Count
                    : 0m;

                var totalWaterM3 = consumption + lossShare;

                // Recommended amounts
                var recWater = Math.Round(totalWaterM3 * settings.WaterPricePerM3, 0);
                var elecCoeff = settings.ElectricityCoefficients.GetValueOrDefault(house.Id, 0);
                var recElectricity = Math.Round(settings.MonthlyElectricityCost * elecCoeff / 100m, 0);
                var recCommon = settings.MonthlyCommonBaseFee;
                var recTotal = recWater + recElectricity + recCommon;

                // Actual (admin override or recommended)
                var over = settings.HouseOverrides.GetValueOrDefault(house.Id);
                var actWater = over?.WaterAdvance ?? recWater;
                var actElec = over?.ElectricityAdvance ?? recElectricity;
                var actCommon = over?.CommonAdvance ?? recCommon;
                var actTotal = actWater + actElec + actCommon;

                houses.Add(new
                {
                    houseId = house.Id,
                    houseName = house.Name,
                    avgMonthlyM3 = Math.Round(consumption, 1),
                    lossShareM3 = Math.Round(lossShare, 1),
                    totalWaterM3 = Math.Round(totalWaterM3, 1),
                    sharePercent = Math.Round(share * 100, 1),
                    electricityCoefficient = elecCoeff,
                    recommended = new { water = recWater, electricity = recElectricity, common = recCommon, total = recTotal },
                    actual = new { water = actWater, electricity = actElec, common = actCommon, total = actTotal },
                    hasOverride = over != null,
                });
            }

            var result = new
            {
                settings = new
                {
                    settings.WaterPricePerM3,
                    settings.WaterPriceValidFrom,
                    settings.WaterPriceValidTo,
                    settings.MonthlyElectricityCost,
                    settings.MonthlyCommonBaseFee,
                    settings.LossAllocationMethod,
                },
                mainMeterMonthlyM3 = Math.Round(mainMonthly, 1),
                totalIndividualMonthlyM3 = Math.Round(totalConsumption, 1),
                monthlyLossM3 = Math.Round(monthlyLoss, 1),
                houses,
            };

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, result);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating advances.");
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
        var json = JsonSerializer.Serialize(new { error = message }, JsonOptions);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(json);
        return response;
    }
}
