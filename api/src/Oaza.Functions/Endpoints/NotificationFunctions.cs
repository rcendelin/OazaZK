using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Interfaces;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class NotificationFunctions
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public NotificationFunctions(
        INotificationService notificationService,
        ILogger<NotificationFunctions> logger)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("SendNotification")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> SendNotificationAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/send")] HttpRequestData req,
        FunctionContext context)
    {
        try
        {
            var user = GetAuthenticatedUser(context);

            var request = await JsonSerializer.DeserializeAsync<SendNotificationRequest>(req.Body, JsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.Type))
            {
                return await WriteErrorResponseAsync(req, 400, "Neplatné tělo požadavku. Pole 'type' je povinné.");
            }

            switch (request.Type)
            {
                case "reading_reminder":
                    await _notificationService.SendReadingReminderAsync();
                    break;

                case "import_completed":
                    if (!request.Year.HasValue || !request.Month.HasValue)
                    {
                        return await WriteErrorResponseAsync(req, 400, "Parametry 'year' a 'month' jsou povinné pro notifikaci import_completed.");
                    }
                    await _notificationService.SendImportNotificationAsync(request.Year.Value, request.Month.Value);
                    break;

                case "settlement_closed":
                    if (string.IsNullOrWhiteSpace(request.PeriodId))
                    {
                        return await WriteErrorResponseAsync(req, 400, "Parametr 'periodId' je povinný pro notifikaci settlement_closed.");
                    }
                    await _notificationService.SendSettlementNotificationAsync(request.PeriodId);
                    break;

                default:
                    return await WriteErrorResponseAsync(req, 400,
                        $"Unknown notification type '{request.Type}'. Valid types: reading_reminder, import_completed, settlement_closed.");
            }

            _logger.LogInformation("Notification of type '{Type}' sent by user {UserId}.", request.Type, user.Id);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK, new { message = "Notification sent successfully." });
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending notification.");
            return await WriteErrorResponseAsync(req, 500, "Nastala neočekávaná chyba.");
        }
    }

    private static User GetAuthenticatedUser(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) &&
            userObj is User user)
        {
            return user;
        }

        throw new AppException("Unauthorized.", 401);
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
}
