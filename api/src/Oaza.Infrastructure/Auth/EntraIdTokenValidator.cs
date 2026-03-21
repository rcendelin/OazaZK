using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Oaza.Application.Auth;

namespace Oaza.Infrastructure.Auth;

public class EntraIdTokenValidator : IEntraIdTokenValidator
{
    private readonly EntraIdSettings _settings;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    public EntraIdTokenValidator(IOptions<EntraIdSettings> settings)
    {
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        _tokenHandler = new JwtSecurityTokenHandler();

        // Entra ID is optional — if not configured, ValidateTokenAsync will always return null
        if (!string.IsNullOrWhiteSpace(_settings.TenantId) && !string.IsNullOrWhiteSpace(_settings.ClientId))
        {
            var metadataAddress =
                $"https://login.microsoftonline.com/{_settings.TenantId}/v2.0/.well-known/openid-configuration";

            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());
        }
    }

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        // Entra ID not configured — cannot validate
        if (_configManager is null)
        {
            return null;
        }

        try
        {
            var config = await _configManager.GetConfigurationAsync(CancellationToken.None);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://login.microsoftonline.com/{_settings.TenantId}/v2.0",
                ValidateAudience = true,
                ValidAudience = _settings.ClientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (Exception)
        {
            // Configuration fetch failure or unexpected token format
            return null;
        }
    }
}
