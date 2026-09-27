using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Application.Mapping;
using Oaza.Application.UseCases;
using Oaza.Application.Validators;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class AdvanceFunctions
{
    private readonly IAdvancePaymentRepository _advanceRepository;
    private readonly IHouseRepository _houseRepository;
    private readonly IBankTransactionRepository _bankTransactionRepository;
    private readonly IClosingBoundary _closingBoundary;
    private readonly ILogger<AdvanceFunctions> _logger;
    private readonly Oaza.Application.Audit.IAuditLogger _audit;

    /// <summary>Audit entity of a house payment (advance, doplatek, payout, legacy opening balance).</summary>
    public const string PaymentEntity = "AdvancePayment";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AdvanceFunctions(
        IAdvancePaymentRepository advanceRepository,
        IHouseRepository houseRepository,
        IBankTransactionRepository bankTransactionRepository,
        IClosingBoundary closingBoundary,
        Oaza.Application.Audit.IAuditLogger audit,
        ILogger<AdvanceFunctions> logger)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _closingBoundary = closingBoundary ?? throw new ArgumentNullException(nameof(closingBoundary));
        _advanceRepository = advanceRepository ?? throw new ArgumentNullException(nameof(advanceRepository));
        _houseRepository = houseRepository ?? throw new ArgumentNullException(nameof(houseRepository));
        _bankTransactionRepository = bankTransactionRepository ?? throw new ArgumentNullException(nameof(bankTransactionRepository));
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
                    return await WriteErrorResponseAsync(req, 403, "Přístup odepřen.");
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
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("CreateAdvance")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateAdvanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "advances")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateAdvanceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
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
                return await WriteErrorResponseAsync(req, 404, $"Domácnost '{request.HouseId}' nebyla nalezena.");
            }

            var rowKey = PaymentRowKeys.Advance(request.Year, request.Month);
            var payment = new AdvancePayment
            {
                HouseId = request.HouseId,
                Year = request.Year,
                Month = request.Month,
                Amount = request.WaterAmount + request.ElectricityAmount + request.CommonAmount,
                WaterAmount = request.WaterAmount,
                ElectricityAmount = request.ElectricityAmount,
                CommonAmount = request.CommonAmount,
                PaymentDate = request.PaymentDate,
                Type = PaymentType.Advance,
                RowKey = rowKey,
            };

            // A month closed by an interim closing: booked as an extra payment on the first open day (T08).
            var shifted = ClosedPeriodPayments.BookAfterClosing(payment, await _closingBoundary.GetLastClosedDayAsync(request.HouseId));

            // Check for duplicate (same house, same year-month)
            if (!shifted && await _advanceRepository.GetAsync(request.HouseId, rowKey) is not null)
            {
                return await WriteErrorResponseAsync(req, 409,
                    $"Zálohová platba pro domácnost '{house.Name}' za {request.Year}-{request.Month:D2} již existuje.");
            }

            await _advanceRepository.UpsertAsync(payment);
            await AuditAsync(context, AuditActions.Create, null, payment);

            _logger.LogInformation("Advance payment created for house {HouseId} for {Year}-{Month}.",
                payment.HouseId, payment.Year, payment.Month);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(payment, house.Name));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("UpdateAdvance")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateAdvanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "advances/{houseId}/{yearMonth}")] HttpRequestData req,
        string houseId,
        string yearMonth,
        FunctionContext context)
    {
        try
        {
            var existing = await _advanceRepository.GetAsync(houseId, yearMonth);
            if (existing is null)
            {
                throw new NotFoundException("AdvancePayment", $"{houseId}/{yearMonth}");
            }

            if (ClosedPeriodPayments.IsClosed(existing, await _closingBoundary.GetLastClosedDayAsync(houseId)))
            {
                return await WriteErrorResponseAsync(req, 409, "Zálohu nelze upravit — měsíc je uzavřený mezizávěrkou.");
            }

            var request = await JsonSerializer.DeserializeAsync<UpdateAdvanceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            }

            var validator = new UpdateAdvanceRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var before = Snapshot(existing);
            existing.WaterAmount = request.WaterAmount;
            existing.ElectricityAmount = request.ElectricityAmount;
            existing.CommonAmount = request.CommonAmount;
            existing.Amount = request.WaterAmount + request.ElectricityAmount + request.CommonAmount;
            existing.PaymentDate = request.PaymentDate;

            await _advanceRepository.UpsertAsync(existing);
            await AuditAsync(context, AuditActions.Update, before, existing);

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
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("CreateDoplatek")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateDoplatekAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "advances/doplatek")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateDoplatekRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            }

            var validator = new CreateDoplatekRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var house = await _houseRepository.GetAsync(PartitionKeys.House, request.HouseId);
            if (house is null)
            {
                return await WriteErrorResponseAsync(req, 404, $"Domácnost '{request.HouseId}' nebyla nalezena.");
            }

            var paymentDate = DateTime.SpecifyKind(request.PaymentDate, DateTimeKind.Utc);

            var payment = new AdvancePayment
            {
                HouseId = request.HouseId,
                Year = paymentDate.Year,
                Month = paymentDate.Month,
                Amount = request.WaterAmount + request.ElectricityAmount + request.CommonAmount,
                WaterAmount = request.WaterAmount,
                ElectricityAmount = request.ElectricityAmount,
                CommonAmount = request.CommonAmount,
                PaymentDate = paymentDate,
                Type = PaymentType.Doplatek,
                Note = request.Note,
                RowKey = PaymentRowKeys.Doplatek(paymentDate),
            };

            ClosedPeriodPayments.BookAfterClosing(payment, await _closingBoundary.GetLastClosedDayAsync(request.HouseId));
            await _advanceRepository.UpsertAsync(payment);
            await AuditAsync(context, AuditActions.Create, null, payment);

            _logger.LogInformation("Doplatek recorded for house {HouseId} ({RowKey}).",
                payment.HouseId, payment.RowKey);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(payment, house.Name));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("CreatePayout")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreatePayoutAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "advances/payout")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreatePayoutRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            }

            var validationResult = await new CreatePayoutRequestValidator().ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var house = await _houseRepository.GetAsync(PartitionKeys.House, request.HouseId);
            if (house is null)
            {
                return await WriteErrorResponseAsync(req, 404, $"Domácnost '{request.HouseId}' nebyla nalezena.");
            }

            var paymentDate = DateTime.SpecifyKind(request.PaymentDate, DateTimeKind.Utc);
            var payment = new AdvancePayment
            {
                HouseId = request.HouseId,
                Year = paymentDate.Year,
                Month = paymentDate.Month,
                Amount = request.Amount,        // positive: refund of overpayment → increases saldo toward zero
                PaymentDate = paymentDate,
                Type = PaymentType.Payout,
                Note = request.Note,
                RowKey = $"V-{InvertedTimestamp.FromDateTime(paymentDate)}-{Guid.NewGuid():N}"[..40],
            };

            ClosedPeriodPayments.BookAfterClosing(payment, await _closingBoundary.GetLastClosedDayAsync(request.HouseId));
            await _advanceRepository.UpsertAsync(payment);
            await AuditAsync(context, AuditActions.Create, null, payment);
            _logger.LogInformation("Payout recorded for house {HouseId} ({RowKey}).", payment.HouseId, payment.RowKey);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(payment, house.Name));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("CreateLegacyOpeningBalancePayment")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateOpeningBalanceAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "advances/opening-balance")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateOpeningBalanceRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
            }

            var validationResult = await new CreateOpeningBalanceRequestValidator().ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            var house = await _houseRepository.GetAsync(PartitionKeys.House, request.HouseId);
            if (house is null)
            {
                return await WriteErrorResponseAsync(req, 404, $"Domácnost '{request.HouseId}' nebyla nalezena.");
            }

            // Signed effect on saldo: overpayment (credit) lowers it, underpayment (debt) raises it.
            var signedAmount = request.IsOverpayment ? -request.Amount : request.Amount;
            var paymentDate = DateTime.SpecifyKind(request.PaymentDate, DateTimeKind.Utc);
            var payment = new AdvancePayment
            {
                HouseId = request.HouseId,
                Year = paymentDate.Year,
                Month = paymentDate.Month,
                Amount = signedAmount,
                PaymentDate = paymentDate,
                Type = PaymentType.OpeningBalance,
                Note = request.Note,
                RowKey = $"O-{InvertedTimestamp.FromDateTime(paymentDate)}-{Guid.NewGuid():N}"[..40],
            };

            await _advanceRepository.UpsertAsync(payment);
            await AuditAsync(context, AuditActions.Create, null, payment);
            _logger.LogInformation("Opening balance recorded for house {HouseId} ({RowKey}).", payment.HouseId, payment.RowKey);

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(payment, house.Name));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    [Function("DeletePayment")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> DeletePaymentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "advances/{houseId}/{rowKey}")] HttpRequestData req,
        string houseId,
        string rowKey,
        FunctionContext context)
    {
        try
        {
            var existing = await _advanceRepository.GetAsync(houseId, rowKey);
            if (existing is null)
            {
                throw new NotFoundException("Payment", $"{houseId}/{rowKey}");
            }

            if (ClosedPeriodPayments.IsClosed(existing, await _closingBoundary.GetLastClosedDayAsync(houseId)))
            {
                return await WriteErrorResponseAsync(req, 409, "Platbu nelze smazat — je v období uzavřeném mezizávěrkou.");
            }

            await _advanceRepository.DeleteAsync(houseId, rowKey);
            await AuditAsync(context, AuditActions.Delete, existing, null);

            // Release the source bank movement so the statement import offers it again.
            if (existing.BankOwnAccountKey is not null && existing.BankTransactionId is not null)
            {
                await _bankTransactionRepository.DeleteAsync(existing.BankOwnAccountKey, existing.BankTransactionId);
            }

            _logger.LogInformation("Payment deleted for house {HouseId} ({RowKey}).", houseId, rowKey);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, new { deleted = true });
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (System.Text.Json.JsonException)
        {
            return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku.");
        }
        catch (Azure.RequestFailedException rfe) when (rfe.Status == 400)
        {
            return await WriteErrorResponseAsync(req, 400, "Hodnotu nelze uložit — je příliš dlouhá nebo neplatná.");
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    private Task AuditAsync(FunctionContext context, string action, AdvancePayment? before, AdvancePayment? after)
    {
        var payment = (after ?? before)!;
        return _audit.LogAsync(PaymentEntity, $"{payment.HouseId}/{payment.RowKey}", action, before, after, ModelEndpoint.GetActor(context));
    }

    private static AdvancePayment Snapshot(AdvancePayment payment) =>
        JsonSerializer.Deserialize<AdvancePayment>(JsonSerializer.Serialize(payment))!;

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) &&
            userObj is User user)
        {
            return user;
        }

        throw new AppException("Uživatel není přihlášen.", 401);
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
            new { error = "Formulář obsahuje chyby.", errors });
    }
}
