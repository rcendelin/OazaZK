using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Audit;
using Oaza.Application.Auth;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Functions.Endpoints;

public class AdvanceSettingsFunctions
{
    /// <summary>Audit entity type of one house's advance override.</summary>
    internal const string AuditEntity = "AdvanceOverride";
    private const string TableName = "AdvanceSettings";
    private const string PartitionKey = "SETTINGS";
    private const string RowKey = "advances";
    /// <summary>Attempts of the optimistic read-modify-write before giving up with 409.</summary>
    internal const int MaxWriteAttempts = 5;

    private readonly TableServiceClient _tableServiceClient;
    private readonly CalculatePrescribedAdvancesUseCase _calculatePrescribedAdvancesUseCase;
    private readonly IAuditLogger _audit;
    private readonly ILogger<AdvanceSettingsFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AdvanceSettingsFunctions(
        TableServiceClient tableServiceClient,
        CalculatePrescribedAdvancesUseCase calculatePrescribedAdvancesUseCase,
        IAuditLogger audit,
        ILogger<AdvanceSettingsFunctions> logger)
    {
        _calculatePrescribedAdvancesUseCase = calculatePrescribedAdvancesUseCase;
        _tableServiceClient = tableServiceClient;
        _audit = audit;
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

    /// <summary>
    /// Replaces the whole override map (kept for compatibility — two admins overwrite each other here; the UI
    /// uses the per-house endpoints below). Every changed house is audited.
    /// </summary>
    [Function("UpdateAdvanceSettings")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateAdvanceSettingsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "advance-settings")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            GetAuthenticatedUser(context);
            var actor = ModelEndpoint.GetActor(context);
            var settings = await JsonSerializer.DeserializeAsync<AdvanceSettings>(req.Body, JsonOptions);
            if (settings is null)
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");

            settings.HouseOverrides ??= new Dictionary<string, HouseAdvanceOverride>();
            if (settings.HouseOverrides.Values.Any(o => !IsValid(o)))
                return await WriteErrorResponseAsync(req, 400, "Zálohy nesmí být záporné.");

            var before = await LoadSettingsAsync();
            var tableClient = _tableServiceClient.GetTableClient(TableName);
            await tableClient.CreateIfNotExistsAsync();
            await tableClient.UpsertEntityAsync(TableEntityMapper.ToTableEntity(settings), TableUpdateMode.Replace);

            foreach (var houseId in before.HouseOverrides.Keys.Union(settings.HouseOverrides.Keys).Order(StringComparer.Ordinal))
            {
                var old = before.HouseOverrides.GetValueOrDefault(houseId);
                var now = settings.HouseOverrides.GetValueOrDefault(houseId);
                if (!SameOverride(old, now))
                    await _audit.LogAsync(AuditEntity, houseId, AuditAction(old, now), old, now, actor);
            }

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

    /// <summary>
    /// Sets the override of one house only: a read-modify-write of the stored map with optimistic concurrency
    /// (ETag), so admins editing different houses never overwrite each other (#11).
    /// </summary>
    [Function("SetHouseAdvanceOverride")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> SetHouseAdvanceOverrideAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "advance-settings/overrides/{houseId}")] HttpRequestData req,
        string houseId,
        FunctionContext context)
    {
        try
        {
            var actor = ModelEndpoint.GetActor(context);
            if (string.IsNullOrWhiteSpace(houseId))
                return await WriteErrorResponseAsync(req, 400, "Chybí dům.");

            HouseAdvanceOverride? value;
            try
            {
                value = await JsonSerializer.DeserializeAsync<HouseAdvanceOverride>(req.Body, JsonOptions);
            }
            catch (JsonException)
            {
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            }
            if (value is null)
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            if (!IsValid(value))
                return await WriteErrorResponseAsync(req, 400, "Zálohy nesmí být záporné.");

            var (old, settings) = await ModifyOverrideAsync(await GetTableAsync(), houseId, value);
            if (!SameOverride(old, value))
                await _audit.LogAsync(AuditEntity, houseId, AuditAction(old, value), old, value, actor);

            _logger.LogInformation("Advance override of house {HouseId} set.", houseId);
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, settings);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting the advance override of house {HouseId}.", houseId);
            return await WriteErrorResponseAsync(req, 500, "Nastala chyba při ukládání zálohy domu. Zkuste to prosím znovu nebo kontaktujte správce.");
        }
    }

    /// <summary>Removes the override of one house (its advance goes back to the recommendation) → 204.</summary>
    [Function("DeleteHouseAdvanceOverride")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> DeleteHouseAdvanceOverrideAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "advance-settings/overrides/{houseId}")] HttpRequestData req,
        string houseId,
        FunctionContext context)
    {
        try
        {
            var actor = ModelEndpoint.GetActor(context);
            var (old, _) = await ModifyOverrideAsync(await GetTableAsync(), houseId, null);
            if (old is not null)
                await _audit.LogAsync(AuditEntity, houseId, AuditActions.Delete, old, null, actor);

            _logger.LogInformation("Advance override of house {HouseId} removed.", houseId);
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (AppException ex) { return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing the advance override of house {HouseId}.", houseId);
            return await WriteErrorResponseAsync(req, 500, "Nastala chyba při ukládání zálohy domu. Zkuste to prosím znovu nebo kontaktujte správce.");
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

    internal static bool IsValid(HouseAdvanceOverride? o) =>
        o is not null && o.WaterAdvance >= 0 && o.ElectricityAdvance >= 0 && o.CommonAdvance >= 0;

    private static bool SameOverride(HouseAdvanceOverride? a, HouseAdvanceOverride? b) =>
        a is null || b is null
            ? a is null && b is null
            : a.WaterAdvance == b.WaterAdvance && a.ElectricityAdvance == b.ElectricityAdvance && a.CommonAdvance == b.CommonAdvance;

    private static string AuditAction(HouseAdvanceOverride? old, HouseAdvanceOverride? now) =>
        old is null ? AuditActions.Create : now is null ? AuditActions.Delete : AuditActions.Update;

    /// <summary>Sets (<paramref name="value"/>) or removes (null) one house in the map; returns its previous override.</summary>
    internal static HouseAdvanceOverride? ApplyOverride(AdvanceSettings settings, string houseId, HouseAdvanceOverride? value)
    {
        var old = settings.HouseOverrides.GetValueOrDefault(houseId);
        if (value is null) settings.HouseOverrides.Remove(houseId);
        else settings.HouseOverrides[houseId] = value;
        return old;
    }

    /// <summary>
    /// Read-modify-write of the stored override map for one house with optimistic concurrency: the update is
    /// conditional on the ETag that was read (a first write is an insert); a concurrent change (412/409) re-reads
    /// and retries, so another house's override written meanwhile is kept.
    /// </summary>
    internal static async Task<(HouseAdvanceOverride? Old, AdvanceSettings Settings)> ModifyOverrideAsync(
        TableClient table, string houseId, HouseAdvanceOverride? value)
    {
        for (var attempt = 1; ; attempt++)
        {
            TableEntity? stored = null;
            try
            {
                stored = (await table.GetEntityAsync<TableEntity>(PartitionKey, RowKey)).Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
            }

            var settings = stored is null ? new AdvanceSettings() : TableEntityMapper.ToAdvanceSettings(stored);
            var old = ApplyOverride(settings, houseId, value);
            if (SameOverride(old, value))
                return (old, settings);

            var entity = TableEntityMapper.ToTableEntity(settings);
            try
            {
                if (stored is null) await table.AddEntityAsync(entity);
                else await table.UpdateEntityAsync(entity, stored.ETag, TableUpdateMode.Replace);
                return (old, settings);
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                if (attempt >= MaxWriteAttempts)
                    throw new AppException("Zálohy právě mění někdo jiný. Zkuste to prosím znovu.", 409);
            }
        }
    }

    private async Task<TableClient> GetTableAsync()
    {
        var tableClient = _tableServiceClient.GetTableClient(TableName);
        await tableClient.CreateIfNotExistsAsync();
        return tableClient;
    }

    private async Task<AdvanceSettings> LoadSettingsAsync()
    {
        var tableClient = await GetTableAsync();
        try
        {
            var response = await tableClient.GetEntityAsync<TableEntity>(PartitionKey, RowKey);
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
