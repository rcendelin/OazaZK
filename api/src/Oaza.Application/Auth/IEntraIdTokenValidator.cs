using System.Security.Claims;

namespace Oaza.Application.Auth;

public interface IEntraIdTokenValidator
{
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token);
}
