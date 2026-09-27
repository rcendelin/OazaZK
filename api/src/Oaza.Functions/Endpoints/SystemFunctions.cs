using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Oaza.Application.Deployment;
using Oaza.Functions.Attributes;

namespace Oaza.Functions.Endpoints;

public class SystemFunctions
{
    private readonly IConfiguration _configuration;

    public SystemFunctions(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Which environment (dev / test / prod / unknown) this API serves. Anonymous
    /// so the login page can show the test-environment banner too.
    /// </summary>
    [Function("GetEnvironment")]
    [AllowAnonymous]
    public async Task<HttpResponseData> GetEnvironmentAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "environment")] HttpRequestData req)
    {
        var environment = DeploymentEnvironment.Normalize(_configuration[DeploymentEnvironment.ConfigKey]);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { environment });
        return response;
    }
}
