using System.Security.Claims;
using Oaza.Domain.Entities;

namespace Oaza.Application.Auth;

public interface IJwtService
{
    string GenerateToken(User user);
    ClaimsPrincipal? ValidateToken(string token);
}
