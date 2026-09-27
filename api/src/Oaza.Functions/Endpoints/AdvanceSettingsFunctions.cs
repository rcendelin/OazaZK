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
                settings.HouseOverrides = new Dictionary<string, HouseAdvanceOverride>();
            }
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, settings);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
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

            settings.HouseOverrides ??= new Dictionary<string, HouseAdvanceOverride>();
            if (settings.HouseOverrides.Values.Any(o => o is null || o.WaterAdvance < 0 || o.ElectricityAdvance < 0 || o.CommonAdvance < 0))
                return await WriteErrorResponseAsync(req, 400, "Zálohy nesmí být záporné.");

            var tableClient = _tableServiceClient.GetTableClient("AdvanceSettings");
            await tableClient.CreateIfNotExistsAsync();
            await tableClient.UpsertEntityAsync(TableEntityMapper.ToTableEntity(settings), TableUpdateMode.Replace);

            _logger.LogInformation("Advance settings updated.");
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, settings);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
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

            // Members only see their own household; admins and accountants see every house.
            var canSeeAllHouses = user.Role is UserRole.Admin or UserRole.Accountant;
            var houses = prescribed.Houses
                .Where(h => canSeeAllHouses || h.HouseId == user.HouseId)
                .Select(h => new
                {
                    houseId = h.HouseId,
                    houseName = h.HouseName,
                    costsInPeriod = Split(h.CostsInPeriod),
                    recommended = Split(h.Recommended),
                    actual = Split(h.Actual),
                    hasOverride = h.HasOverride,
                })
                .ToList();

            var result = new
            {
                from = prescribed.Period.From.ToString("yyyy-MM-dd"),
                to = prescribed.Period.To.ToString("yyyy-MM-dd"),
                months = prescribed.Months,
                houses,
            };

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, result);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating advances.");
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    private static object Split(AdvanceSplit s) =>
        new { water = s.Water, electricity = s.Electricity, common = s.Common, total = s.Total };

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
