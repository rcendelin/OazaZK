using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Application.Mapping;
using Oaza.Application.UseCases;
using Oaza.Application.Validators;
using Oaza.Domain.Entities;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class AuthFunctions
{
    private readonly RequestMagicLinkUseCase _requestMagicLinkUseCase;
    private readonly VerifyMagicLinkUseCase _verifyMagicLinkUseCase;
    private readonly ILogger<AuthFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public AuthFunctions(
        RequestMagicLinkUseCase requestMagicLinkUseCase,
        VerifyMagicLinkUseCase verifyMagicLinkUseCase,
        ILogger<AuthFunctions> logger)
    {
        _requestMagicLinkUseCase = requestMagicLinkUseCase ?? throw new ArgumentNullException(nameof(requestMagicLinkUseCase));
        _verifyMagicLinkUseCase = verifyMagicLinkUseCase ?? throw new ArgumentNullException(nameof(verifyMagicLinkUseCase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetMe")]
    public async Task<HttpResponseData> GetMeAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/me")] HttpRequestData req,
        FunctionContext context)
    {
        if (!context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) ||
            userObj is not User user)
        {
            return await WriteJsonResponseAsync(req, HttpStatusCode.Unauthorized,
                new { error = "Unauthorized" });
        }

        return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
            EntityMapper.ToResponse(user));
    }

    [Function("RequestMagicLink")]
    [AllowAnonymous]
    public async Task<HttpResponseData> RequestMagicLink(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/magic-link")] HttpRequestData req)
    {
        MagicLinkRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<MagicLinkRequest>(req.Body, JsonOptions);
        }
        catch (JsonException)
        {
            return await WriteJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = "Invalid request body." });
        }

        if (request is null)
        {
            return await WriteJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = "Request body is required." });
        }

        var validator = new MagicLinkRequestValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return await WriteJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = validationResult.Errors[0].ErrorMessage });
        }

        await _requestMagicLinkUseCase.ExecuteAsync(request.Email);

        // Always return success to prevent email enumeration
        return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
            new { message = "If the email is registered, a login link has been sent." });
    }

    [Function("VerifyMagicLink")]
    [AllowAnonymous]
    public async Task<HttpResponseData> VerifyMagicLink(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/magic-link/verify")] HttpRequestData req)
    {
        MagicLinkVerifyRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<MagicLinkVerifyRequest>(req.Body, JsonOptions);
        }
        catch (JsonException)
        {
            return await WriteJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = "Invalid request body." });
        }

        if (request is null)
        {
            return await WriteJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = "Request body is required." });
        }

        var validator = new MagicLinkVerifyRequestValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return await WriteJsonResponseAsync(req, HttpStatusCode.BadRequest,
                new { error = validationResult.Errors[0].ErrorMessage });
        }

        var authResponse = await _verifyMagicLinkUseCase.ExecuteAsync(request.Token, request.Email);

        if (authResponse is null)
        {
            return await WriteJsonResponseAsync(req, HttpStatusCode.Unauthorized,
                new { error = "Invalid or expired token." });
        }

        return await WriteJsonResponseAsync(req, HttpStatusCode.OK, authResponse);
    }

    private static async Task<HttpResponseData> WriteJsonResponseAsync<T>(
        HttpRequestData req, HttpStatusCode statusCode, T body)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        var json = JsonSerializer.Serialize(body, JsonOptions);
        await response.WriteStringAsync(json);
        return response;
    }
}
