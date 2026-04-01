using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Oaza.Application.DTOs;
using Oaza.Application.Exceptions;
using Oaza.Application.Mapping;
using Oaza.Application.Validators;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Oaza.Application.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class UserFunctions
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly string _appUrl;
    private readonly ILogger<UserFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public UserFunctions(
        IUserRepository userRepository,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<UserFunctions> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _appUrl = configuration["AppUrl"] ?? "https://oaza.cendelinovi.cz";
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [Function("GetUsers")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> GetUsersAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users")] HttpRequestData req)
    {
        try
        {
            var users = await _userRepository.GetByPartitionKeyAsync(PartitionKeys.User);
            return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                users.Select(EntityMapper.ToResponse).ToList());
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("CreateUser")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> CreateUserAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users")] HttpRequestData req)
    {
        try
        {
            var request = await JsonSerializer.DeserializeAsync<CreateUserRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new CreateUserRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            // Check for duplicate email
            var existingUser = await _userRepository.GetByEmailAsync(request.Email);
            if (existingUser is not null)
            {
                return await WriteErrorResponseAsync(req, 409, $"A user with email '{request.Email}' already exists.");
            }

            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var userRole))
            {
                return await WriteErrorResponseAsync(req, 400, $"Invalid role: {request.Role}");
            }

            if (!Enum.TryParse<AuthMethod>(request.AuthMethod, ignoreCase: true, out var authMethod))
            {
                return await WriteErrorResponseAsync(req, 400, $"Invalid auth method: {request.AuthMethod}");
            }

            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                Email = request.Email,
                Role = userRole,
                HouseId = request.HouseId,
                AuthMethod = authMethod,
                NotificationsEnabled = true,
            };

            await _userRepository.UpsertAsync(user);

            _logger.LogInformation("User {UserId} created: {Email} with role {Role}.", user.Id, user.Email, user.Role);

            // Send invitation email (don't block user creation on email failure)
            try
            {
                var loginUrl = $"{_appUrl}/login";
                var subject = "Pozvánka do portálu Oáza Zadní Kopanina";

                var plainText = $"""
                    Dobrý den {user.Name},

                    byli jste přidáni do portálu Oáza Zadní Kopanina jako {(user.Role == UserRole.Admin ? "administrátor" : user.Role == UserRole.Accountant ? "účetní" : "člen")}.

                    Pro přihlášení navštivte: {loginUrl}

                    S pozdravem,
                    Portál Oáza Zadní Kopanina
                    """;

                var html = $"""
                    <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                        <h2 style="color: #4f46e5;">Pozvánka do portálu Oáza</h2>
                        <p>Dobrý den {System.Net.WebUtility.HtmlEncode(user.Name)},</p>
                        <p>byli jste přidáni do portálu Oáza Zadní Kopanina jako <strong>{(user.Role == UserRole.Admin ? "administrátor" : user.Role == UserRole.Accountant ? "účetní" : "člen")}</strong>.</p>
                        <p style="text-align: center; margin: 32px 0;">
                            <a href="{System.Net.WebUtility.HtmlEncode(loginUrl)}"
                               style="background-color: #4f46e5; color: white; padding: 12px 32px;
                                      text-decoration: none; border-radius: 6px; font-weight: bold;
                                      display: inline-block;">
                                Přihlásit se
                            </a>
                        </p>
                        <hr style="border: none; border-top: 1px solid #e4e4e7; margin: 24px 0;" />
                        <p style="color: #a1a1aa; font-size: 12px;">Portál Oáza Zadní Kopanina</p>
                    </div>
                    """;

                await _emailService.SendEmailAsync(user.Email, user.Name, subject, plainText, html);
                _logger.LogInformation("Invitation email sent to {Email}.", user.Email);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Failed to send invitation email to {Email}. User was created successfully.", user.Email);
            }

            return await WriteJsonResponseAsync(req, HttpStatusCode.Created,
                EntityMapper.ToResponse(user));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("UpdateUser")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> UpdateUserAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await _userRepository.GetAsync(PartitionKeys.User, id);
            if (existing is null)
            {
                throw new NotFoundException("User", id);
            }

            var request = await JsonSerializer.DeserializeAsync<UpdateUserRequest>(req.Body, JsonOptions);
            if (request is null)
            {
                return await WriteErrorResponseAsync(req, 400, "Invalid request body.");
            }

            var validator = new UpdateUserRequestValidator();
            var validationResult = await validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return await WriteValidationErrorResponseAsync(req, validationResult);
            }

            existing.Name = request.Name;

            if (request.Role is not null)
            {
                if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var updatedRole))
                {
                    return await WriteErrorResponseAsync(req, 400, $"Invalid role: {request.Role}");
                }
                existing.Role = updatedRole;
            }

            existing.HouseId = request.HouseId; // null clears the assignment

            if (request.NotificationsEnabled.HasValue)
            {
                existing.NotificationsEnabled = request.NotificationsEnabled.Value;
            }

            await _userRepository.UpsertAsync(existing);

            _logger.LogInformation("User {UserId} updated.", id);

            return await WriteJsonResponseAsync(req, HttpStatusCode.OK,
                EntityMapper.ToResponse(existing));
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
    }

    [Function("DeleteUser")]
    [RequireRole(UserRole.Admin)]
    public async Task<HttpResponseData> DeleteUserAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/{id}")] HttpRequestData req,
        string id)
    {
        try
        {
            var existing = await _userRepository.GetAsync(PartitionKeys.User, id);
            if (existing is null)
            {
                throw new NotFoundException("User", id);
            }

            // Prevent admin from deleting themselves
            var currentUserId = req.FunctionContext.Items.TryGetValue("UserId", out var uid) ? uid as string : null;
            if (currentUserId == id)
            {
                return await WriteErrorResponseAsync(req, 400, "Nemůžete smazat sami sebe.");
            }

            await _userRepository.DeleteAsync(PartitionKeys.User, id);

            _logger.LogInformation("User {UserId} ({Email}) deleted.", id, existing.Email);

            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (AppException ex)
        {
            return await WriteErrorResponseAsync(req, ex.StatusCode, ex.Message);
        }
        catch (Exception)
        {
            return await WriteErrorResponseAsync(req, 500, "An unexpected error occurred.");
        }
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
            new { error = "Validation failed.", errors });
    }
}
