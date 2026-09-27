using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Functions.Endpoints;

public class AdvanceSettingsFunctions
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly CalculatePrescribedAdvancesUseCase _calculatePrescribedAdvancesUseCase;
    private readonly ILogger<AdvanceSettingsFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AdvanceSettingsFunctions(
        TableServiceClient tableServiceClient,
        CalculatePrescribedAdvancesUseCase calculatePrescribedAdvancesUseCase,
        ILogger<AdvanceSettingsFunctions> logger)
    {
        _calculatePrescribedAdvancesUseCase = calculatePrescribedAdvancesUseCase;
        _tableServiceClient = tableServiceClient;
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
            // Members may not see other households' per-house pricing details.
            if (user.Role == UserRole.Member)
            {
                settings.ElectricityCoefficients = new Dictionary<string, decimal>();
                settings.HouseOverrides = new Dictionary<string, HouseAdvanceOverride>();
            }
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, settings);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Advance settings error.");
            return await WriteErrorResponseAsync(req, 500, "Nastala chyba při načítání nastavení záloh. Zkuste to prosím znovu nebo kontaktujte správce.");
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
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");

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
            return await WriteErrorResponseAsync(req, 500, "Nastala chyba při ukládání nastavení záloh. Zkuste to prosím znovu nebo kontaktujte správce.");
        }
    }

    [Function("CalculateAdvances")]
    public async Task<HttpResponseData> CalculateAdvancesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "advance-settings/calculate")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            var prescribed = await _calculatePrescribedAdvancesUseCase.CalculateAsync();
            var settings = prescribed.Settings;

            // Members only see their own household; admins and accountants see every house.
            var canSeeAllHouses = user.Role is UserRole.Admin or UserRole.Accountant;
            var houses = prescribed.Houses
                .Where(h => canSeeAllHouses || h.HouseId == user.HouseId)
                .Select(h => new
                {
                    houseId = h.HouseId,
                    houseName = h.HouseName,
                    avgMonthlyM3 = Math.Round(h.AvgMonthlyM3, 1),
                    lossShareM3 = Math.Round(h.LossShareM3, 1),
                    totalWaterM3 = Math.Round(h.TotalWaterM3, 1),
                    sharePercent = Math.Round(h.Share * 100, 1),
                    electricityCoefficient = h.ElectricityCoefficient,
                    recommended = new { water = h.Recommended.Water, electricity = h.Recommended.Electricity, common = h.Recommended.Common, total = h.Recommended.Total },
                    actual = new { water = h.Actual.Water, electricity = h.Actual.Electricity, common = h.Actual.Common, total = h.Actual.Total },
                    hasOverride = h.HasOverride,
                })
                .ToList();

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
                mainMeterMonthlyM3 = Math.Round(prescribed.MainMeterMonthlyM3, 1),
                totalIndividualMonthlyM3 = Math.Round(prescribed.TotalIndividualMonthlyM3, 1),
                monthlyLossM3 = Math.Round(prescribed.MonthlyLossM3, 1),
                houses,
            };

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, result);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating advances.");
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
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
        throw new AppException("Uživatel není přihlášen.", 401);
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
