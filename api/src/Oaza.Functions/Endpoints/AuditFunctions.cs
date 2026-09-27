using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;
using Oaza.Domain.Time;

namespace Oaza.Functions.Endpoints;

public class AuditFunctions
{
    /// <summary>Longest range one request may read (each month is one partition query).</summary>
    private const int MaxRangeDays = 731;

    private readonly IAuditLogRepository _repository;
    private readonly IClock _clock;
    private readonly ILogger<AuditFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AuditFunctions(IAuditLogRepository repository, IClock clock, ILogger<AuditFunctions> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// GET /audit-log?from=yyyy-MM-dd&amp;to=yyyy-MM-dd&amp;entityType=&amp;entityId= — newest first.
    /// Defaults to the last 90 days; <c>to</c> is inclusive (whole day).
    /// </summary>
    [Function("GetAuditLog")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> GetAuditLogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "audit-log")] HttpRequestData req)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var today = PragueClock.AsUtcMidnight(_clock.Today);

            if (!TryParseDate(query["from"], today.AddDays(-90), out var from) ||
                !TryParseDate(query["to"], today, out var to))
            {
                return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { error = "Datum zadejte ve tvaru RRRR-MM-DD." });
            }

            if (to < from)
            {
                return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { error = "Datum „od“ musí být před datem „do“." });
            }

            if ((to - from).TotalDays > MaxRangeDays)
            {
                return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { error = "Rozsah může být nejvýš 2 roky." });
            }

            var entries = await _repository.QueryAsync(
                from,
                to.AddDays(1).AddTicks(-1),
                string.IsNullOrWhiteSpace(query["entityType"]) ? null : query["entityType"],
                string.IsNullOrWhiteSpace(query["entityId"]) ? null : query["entityId"]);

            var response = entries.Select(e => new AuditLogEntryResponse
            {
                Id = e.Id,
                Timestamp = e.Timestamp,
                UserId = e.UserId,
                UserName = e.UserName,
                EntityType = e.EntityType,
                EntityId = e.EntityId,
                Action = e.Action,
                OldValue = e.OldValue,
                NewValue = e.NewValue,
                Reason = e.Reason,
            }).ToList();

            return await WriteJsonAsync(req, HttpStatusCode.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading audit log.");
            return await WriteJsonAsync(req, HttpStatusCode.InternalServerError, new { error = "Nastala neočekávaná chyba." });
        }
    }

    private static bool TryParseDate(string? value, DateTime fallback, out DateTime date)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            date = fallback;
            return true;
        }

        var ok = DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed);
        date = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return ok;
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(HttpRequestData req, HttpStatusCode status, T body)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }
}
