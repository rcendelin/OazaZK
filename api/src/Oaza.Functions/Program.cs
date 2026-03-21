using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Oaza.Application.Auth;
using Oaza.Infrastructure;
using Oaza.Infrastructure.Auth;
using Oaza.Functions.Middleware;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(workerApp =>
    {
        workerApp.UseMiddleware<AuthenticationMiddleware>();
        workerApp.UseMiddleware<AuthorizationMiddleware>();
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Auth settings from configuration
        services.Configure<JwtSettings>(options =>
        {
            options.Secret = configuration["JwtSecret"] ?? string.Empty;
            options.Issuer = configuration["JwtIssuer"] ?? string.Empty;
        });

        services.Configure<EntraIdSettings>(options =>
        {
            options.TenantId = configuration["EntraId__TenantId"] ?? string.Empty;
            options.ClientId = configuration["EntraId__ClientId"] ?? string.Empty;
        });

        // Auth services
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IEntraIdTokenValidator, EntraIdTokenValidator>();

        // Infrastructure: Table Storage, Blob Storage, all repositories
        services.AddInfrastructure(configuration);
    })
    .Build();

host.Run();
