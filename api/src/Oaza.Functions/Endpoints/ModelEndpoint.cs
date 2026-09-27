using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Audit;
using Oaza.Application.Auth;
using Oaza.Application.Exceptions;
using Oaza.Domain.Entities;

namespace Oaza.Functions.Endpoints;

/// <summary>
/// Shared plumbing of the new-model endpoints (T02+): JSON with string enums and <c>yyyy-MM-dd</c> days,
/// the audit actor from the signed-in user, and one error mapping — business rules → 400 with every reason
/// in <c>errors</c>, <see cref="AppException"/> → its status, anything else → 500.
/// </summary>
internal static class ModelEndpoint
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<HttpResponseData> HandleAsync<T>(
        HttpRequestData req, ILogger logger, Func<Task<T>> action, HttpStatusCode success = HttpStatusCode.OK)
    {
        try
        {
            var result = await action();
            if (success == HttpStatusCode.NoContent)
                return req.CreateResponse(HttpStatusCode.NoContent);
            return await WriteJsonAsync(req, success, result);
        }
        catch (JsonException)
        {
            return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { error = "Neplatné tělo požadavku." });
        }
        catch (BusinessRuleException ex)
        {
            var errors = ex.Errors.Select(message => new { field = string.Empty, message }).ToList();
            return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { error = ex.Message, errors });
        }
        catch (AppException ex)
        {
            return await WriteJsonAsync(req, (HttpStatusCode)ex.StatusCode, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in endpoint {Url}.", req.Url);
            return await WriteJsonAsync(req, HttpStatusCode.InternalServerError, new { error = "Nastala neočekávaná chyba." });
        }
    }

    /// <summary>Like <see cref="HandleAsync{T}"/>, but answers with a downloadable file.</summary>
    public static async Task<HttpResponseData> HandleFileAsync(
        HttpRequestData req, ILogger logger, Func<Task<(byte[] Content, string ContentType, string FileName)>> action)
    {
        try
        {
            var (content, contentType, fileName) = await action();
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", contentType);
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            await response.WriteBytesAsync(content);
            return response;
        }
        catch (ArgumentException ex)
        {
            return await WriteJsonAsync(req, HttpStatusCode.BadRequest, new { error = ex.Message.Split(" (Parameter")[0] });
        }
        catch (AppException ex)
        {
            return await WriteJsonAsync(req, (HttpStatusCode)ex.StatusCode, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in export {Url}.", req.Url);
            return await WriteJsonAsync(req, HttpStatusCode.InternalServerError, new { error = "Nastala neočekávaná chyba." });
        }
    }

    public static async Task<T> ReadAsync<T>(HttpRequestData req) where T : class =>
        await JsonSerializer.DeserializeAsync<T>(req.Body, JsonOptions) ?? throw new AppException("Chybí tělo požadavku.");

    public static string? Query(HttpRequestData req, string name) =>
        System.Web.HttpUtility.ParseQueryString(req.Url.Query)[name];

    /// <summary>Parses a calendar day <c>yyyy-MM-dd</c>.</summary>
    public static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    public static AuditActor GetActor(FunctionContext context)
    {
        if (context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) && userObj is User user)
            return new AuditActor(user.Id, user.Name);
        throw new AppException("Uživatel není přihlášen.", 401);
    }

    private static async Task<HttpResponseData> WriteJsonAsync<T>(HttpRequestData req, HttpStatusCode statusCode, T body)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }
}
