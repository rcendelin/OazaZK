using System.Net;
using System.Reflection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Domain.Entities;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Middleware;

public class AuthorizationMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ILogger<AuthorizationMiddleware> _logger;

    public AuthorizationMiddleware(ILogger<AuthorizationMiddleware> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var targetMethod = GetTargetMethod(context);
        if (targetMethod is null)
        {
            // Cannot resolve method — deny by default for safety
            var httpReq = await context.GetHttpRequestDataAsync();
            if (httpReq is not null)
            {
                await WriteForbiddenResponseAsync(context, httpReq);
            }
            return;
        }

        // If the endpoint has [AllowAnonymous], skip authorization
        if (targetMethod.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
        {
            await next(context);
            return;
        }

        // Check if a [RequireRole] attribute is present
        var requireRoleAttr = targetMethod.GetCustomAttribute<RequireRoleAttribute>();
        if (requireRoleAttr is null)
        {
            // No specific role required — any authenticated user is fine
            await next(context);
            return;
        }

        // Get the authenticated user from context (set by AuthenticationMiddleware)
        if (!context.Items.TryGetValue(AuthConstants.HttpContextUserKey, out var userObj) ||
            userObj is not User user)
        {
            // No authenticated user — AuthenticationMiddleware should have caught this,
            // but be defensive
            var httpRequestData = await context.GetHttpRequestDataAsync();
            if (httpRequestData is not null)
            {
                await WriteForbiddenResponseAsync(context, httpRequestData);
            }
            return;
        }

        // Check if user's role matches any of the required roles
        if (!requireRoleAttr.Roles.Contains(user.Role))
        {
            _logger.LogWarning(
                "User {UserId} with role {Role} attempted to access {Endpoint} requiring {RequiredRoles}.",
                user.Id, user.Role, context.FunctionDefinition.Name,
                string.Join(", ", requireRoleAttr.Roles));

            var httpRequestData = await context.GetHttpRequestDataAsync();
            if (httpRequestData is not null)
            {
                await WriteForbiddenResponseAsync(context, httpRequestData);
            }
            return;
        }

        await next(context);
    }

    private static MethodInfo? GetTargetMethod(FunctionContext context)
    {
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

    private static async Task WriteForbiddenResponseAsync(FunctionContext context, HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.Forbidden);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync("{\"error\":\"Insufficient permissions\"}");
        context.GetInvocationResult().Value = response;
    }
}
