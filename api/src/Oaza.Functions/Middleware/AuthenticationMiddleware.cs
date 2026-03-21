using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Domain.Constants;
using Oaza.Domain.Enums;
using Oaza.Domain.Interfaces;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Middleware;

public class AuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private readonly IJwtService _jwtService;
    private readonly IEntraIdTokenValidator _entraIdTokenValidator;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AuthenticationMiddleware> _logger;

    private static readonly HashSet<string> AnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/magic-link",
        "/api/auth/magic-link/verify"
    };

    public AuthenticationMiddleware(
        IJwtService jwtService,
        IEntraIdTokenValidator entraIdTokenValidator,
        IUserRepository userRepository,
        ILogger<AuthenticationMiddleware> logger)
    {
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        _entraIdTokenValidator = entraIdTokenValidator ?? throw new ArgumentNullException(nameof(entraIdTokenValidator));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (await IsAnonymousEndpointAsync(context))
        {
            await next(context);
            return;
        }

        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData is null)
        {
            // Non-HTTP trigger (e.g., timer) — skip auth
            await next(context);
            return;
        }

        var token = ExtractBearerToken(httpRequestData);
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("Missing Authorization header for {Path}.", httpRequestData.Url.AbsolutePath);
            await WriteUnauthorizedResponseAsync(context, httpRequestData);
            return;
        }

        // Try custom JWT first (magic link tokens)
        var principal = _jwtService.ValidateToken(token);

        // If custom JWT fails, try Entra ID token
        if (principal is null)
        {
            principal = await _entraIdTokenValidator.ValidateTokenAsync(token);
        }

        if (principal is null)
        {
            _logger.LogWarning("Invalid token for {Path}.", httpRequestData.Url.AbsolutePath);
            await WriteUnauthorizedResponseAsync(context, httpRequestData);
            return;
        }

        // Resolve user from token claims
        var user = await ResolveUserAsync(principal);
        if (user is null)
        {
            _logger.LogWarning("Authenticated token but no matching user found for {Path}.", httpRequestData.Url.AbsolutePath);
            await WriteForbiddenResponseAsync(context, httpRequestData);
            return;
        }

        // Store authenticated user in context for downstream use
        context.Items[AuthConstants.HttpContextUserKey] = user;

        await next(context);
    }

    private static async Task<bool> IsAnonymousEndpointAsync(FunctionContext context)
    {
        // Check for [AllowAnonymous] attribute on the function method
        var targetMethod = GetTargetMethod(context);
        if (targetMethod?.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
        {
            return true;
        }

        // If method can't be resolved, treat as requiring auth (not anonymous)
        if (targetMethod is null)
        {
            return false;
        }

        // Fallback: check path-based list
        var httpRequestData = await context.GetHttpRequestDataAsync();
        if (httpRequestData is not null)
        {
            var path = httpRequestData.Url.AbsolutePath;
            return AnonymousPaths.Contains(path);
        }

        return false;
    }

    private static MethodInfo? GetTargetMethod(FunctionContext context)
    {
        // Get the entry point from function definition
        var entryPoint = context.FunctionDefinition.EntryPoint;
        var lastDot = entryPoint.LastIndexOf('.');
        if (lastDot < 0) return null;

        var typeName = entryPoint[..lastDot];
        var methodName = entryPoint[(lastDot + 1)..];

        // Search all loaded assemblies — Assembly.GetEntryAssembly() may not
        // return the correct assembly in Azure Functions Isolated Worker
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type is not null)
            {
                return type.GetMethod(methodName);
            }
        }

        return null;
    }

    private static string? ExtractBearerToken(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues("Authorization", out var values))
        {
            return null;
        }

        var authHeader = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return null;
        }

        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        return null;
    }

    private async Task<Domain.Entities.User?> ResolveUserAsync(ClaimsPrincipal principal)
    {
        // Try to resolve by our custom claim (sub = user ID) for magic link JWT
        var userId = principal.FindFirstValue(AuthConstants.ClaimUserId);
        if (!string.IsNullOrEmpty(userId))
        {
            var user = await _userRepository.GetAsync(PartitionKeys.User, userId);
            if (user is not null) return user;
        }

        // Try to resolve by Entra ID object ID (oid claim)
        var entraObjectId = principal.FindFirstValue("oid")
                            ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");
        if (!string.IsNullOrEmpty(entraObjectId))
        {
            var user = await _userRepository.GetByEntraObjectIdAsync(entraObjectId);
            if (user is not null) return user;
        }

        // Try to resolve by email as last resort
        var email = principal.FindFirstValue("email")
                    ?? principal.FindFirstValue("preferred_username")
                    ?? principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(email))
        {
            return await _userRepository.GetByEmailAsync(email);
        }

        return null;
    }

    private static async Task WriteUnauthorizedResponseAsync(FunctionContext context, HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Unauthorized);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync("{\"error\":\"Unauthorized\"}");
        context.GetInvocationResult().Value = response;
    }

    private static async Task WriteForbiddenResponseAsync(FunctionContext context, HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Forbidden);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync("{\"error\":\"User not registered in the system\"}");
        context.GetInvocationResult().Value = response;
    }
}
