using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Oaza.Application.Auth;
using Oaza.Domain.Entities;

namespace Oaza.Infrastructure.Auth;

public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;

    public JwtService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));

        if (string.IsNullOrWhiteSpace(_settings.Secret) || _settings.Secret.Length < 32)
        {
            throw new InvalidOperationException(
                "JWT secret must be at least 32 characters (256 bits).");
        }

        if (string.IsNullOrWhiteSpace(_settings.Issuer))
        {
            throw new InvalidOperationException("JWT issuer must be configured.");
        }

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var audience = string.IsNullOrWhiteSpace(_settings.Audience) ? _settings.Issuer : _settings.Audience;

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    public string GenerateToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var claims = new List<Claim>
        {
            new(AuthConstants.ClaimUserId, user.Id),
            new(AuthConstants.ClaimEmail, user.Email),
            new(AuthConstants.ClaimRole, user.Role.ToString()),
            new(AuthConstants.ClaimAuthMethod, user.AuthMethod.ToString())
        };

        if (!string.IsNullOrEmpty(user.HouseId))
        {
            claims.Add(new Claim(AuthConstants.ClaimHouseId, user.HouseId));
        }

        var audience = string.IsNullOrWhiteSpace(_settings.Audience) ? _settings.Issuer : _settings.Audience;

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(_settings.ExpiryHours),
            Issuer = _settings.Issuer,
            Audience = audience,
            SigningCredentials = _signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, _validationParameters, out _);
            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
