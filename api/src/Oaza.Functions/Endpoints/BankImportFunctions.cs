using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.BankImport;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.UseCases;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

/// <summary>
/// Bank statement import (Fio CSV → household payments) and management of the
/// household bank accounts used to assign incoming payments to houses.
/// </summary>
public class BankImportFunctions
{
    private readonly ImportBankStatementUseCase _importUseCase;
    private readonly IBankAccountMappingRepository _mappingRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly ILogger<BankImportFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public BankImportFunctions(
        ImportBankStatementUseCase importUseCase,
        IBankAccountMappingRepository mappingRepository,
        IHouseRepository houseRepository,
        ILogger<BankImportFunctions> logger)
    {
        _importUseCase = importUseCase ?? throw new ArgumentNullException(nameof(importUseCase));
        _mappingRepository = mappingRepository ?? throw new ArgumentNullException(nameof(mappingRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Parses an uploaded statement (raw request body) and returns suggestions. Saves nothing.</summary>
    [Function("PreviewBankImport")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> PreviewAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bank-import/preview")] HttpRequestData req)
    {
        try
        {
            var content = await ReadBodyWithLimitAsync(req.Body, FioCsvParser.MaxFileSizeBytes);
            if (content.Length == 0)
            {
                return await WriteErrorResponseAsync(req, 400, "Soubor je prázdný.");
            }

            var preview = await _importUseCase.PreviewAsync(content);
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, preview);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during bank statement preview.");
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba při čtení výpisu.");
        }
    }

    [Function("ConfirmBankImport")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> ConfirmAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bank-import/confirm")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);
            var request = await JsonSerializer.DeserializeAsync<ConfirmBankImportRequest>(req.Body, JsonOptions);
            if (request is null || request.Rows.Count == 0)
            {
                return await WriteErrorResponseAsync(req, 400, "Nejsou žádné platby k importu.");
            }

            var result = await _importUseCase.ConfirmAsync(request, user.Id);
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, result);
        }
        catch (JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during bank import confirmation.");
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba při ukládání plateb.");
        }
    }

    [Function("GetBankAccounts")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> GetBankAccountsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bank-accounts")] HttpRequestData req)
    {
        try
        {
            var mappings = await _mappingRepository.GetAllMappingsAsync();
            var response = mappings
                .OrderBy(m => m.AccountKey, StringComparer.Ordinal)
                .Select(ToResponse)
                .ToList();
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing bank accounts.");
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("CreateBankAccount")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateBankAccountAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bank-accounts")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateBankAccountRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            }

            if (!BankAccountNumber.TryParse(request.AccountNumber, out var account))
            {
                return await WriteErrorResponseAsync(req, 400,
                    "Neplatné číslo účtu. Zadejte ho ve tvaru [předčíslí-]číslo/kód banky, např. 123456789/0300.");
            }

            var house = await _houseRepository.GetAsync(PartitionKeys.House, request.HouseId);
            if (house is null)
            {
                return await WriteErrorResponseAsync(req, 404, "Domácnost nebyla nalezena.");
            }

            var key = account!.ToKey();
            var existing = await _mappingRepository.GetAsync(PartitionKeys.BankAccountMapping, key);
            if (existing is not null && existing.HouseId != house.Id)
            {
                var owner = await _houseRepository.GetAsync(PartitionKeys.House, existing.HouseId);
                return await WriteErrorResponseAsync(req, 409,
                    $"Účet {account} už je přiřazený domácnosti „{owner?.Name ?? existing.HouseId}“. Nejdřív ho u ní odeberte.");
            }

            var mapping = new BankAccountMapping
            {
                AccountKey = key,
                HouseId = house.Id,
                AccountName = string.IsNullOrWhiteSpace(request.AccountName) ? existing?.AccountName : request.AccountName.Trim(),
                UpdatedAt = DateTime.UtcNow,
            };
            await _mappingRepository.UpsertAsync(mapping);

            _logger.LogInformation("Bank account {AccountKey} assigned to house {HouseId}.", key, house.Id);
            return await WriteJsonResponseAsync(req, HttpStatusCode.Created, ToResponse(mapping));
        }
        catch (JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating bank account mapping.");
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("DeleteBankAccount")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> DeleteBankAccountAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "bank-accounts/{accountKey}")] HttpRequestData req,
        string accountKey)
    {
        try
        {
            await _mappingRepository.DeleteAsync(PartitionKeys.BankAccountMapping, accountKey);
            _logger.LogInformation("Bank account {AccountKey} unassigned.", accountKey);
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, new { deleted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting bank account mapping.");
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    private static BankAccountResponse ToResponse(BankAccountMapping m) => new()
    {
        AccountKey = m.AccountKey,
        AccountNumber = BankAccountNumber.KeyToDisplay(m.AccountKey),
        HouseId = m.HouseId,
        AccountName = m.AccountName,
        UpdatedAt = m.UpdatedAt,
    };

    private static async Task<byte[]> ReadBodyWithLimitAsync(Stream body, long maxBytes)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while ((read = await body.ReadAsync(buffer)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new AppException($"Soubor přesahuje maximální velikost {maxBytes / 1024} kB.");
            }
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) && userObj is User user)
        {
            return user;
        }

        throw new AppException("Uživatel není přihlášen.", 401);
    }

    private static async Task<HttpResponseData> WriteJsonResponseAsync<T>(HttpRequestData req, HttpStatusCode statusCode, T body)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }

    private static Task<HttpResponseData> WriteErrorResponseAsync(HttpRequestData req, int statusCode, string message) =>
        WriteJsonResponseAsync(req, (HttpStatusCode)statusCode, new { error = message });
}
