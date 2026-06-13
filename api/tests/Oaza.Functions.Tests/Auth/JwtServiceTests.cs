using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Oaza.Application.Auth;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Infrastructure.Auth;

namespace Oaza.Functions.Tests.Auth;

public class JwtServiceTests
{
    private static JwtService CreateService() =>
        new(Options.Create(new JwtSettings
        {
            Secret = "this-is-a-very-long-test-secret-key-0123456789!!",
            Issuer = "oaza-test",
            ExpiryHours = 24,
        }));

    [Fact]
    public void GenerateThenValidate_PreservesShortClaimNames()
    {
        // Regression: with MapInboundClaims=true (the handler default) the "sub"
        // claim is remapped to ClaimTypes.NameIdentifier, so the middleware's
        // primary lookup by "sub" returned null. MapInboundClaims=false keeps the
        // short names intact.
        var service = CreateService();
        var user = new User
        {
            Id = "user-123",
            Email = "tester@oaza.cz",
            Role = UserRole.Member,
            HouseId = "house-9",
            AuthMethod = AuthMethod.MagicLink,
        };

        var token = service.GenerateToken(user);
        var principal = service.ValidateToken(token);

        principal.Should().NotBeNull();
        principal!.FindFirstValue(AuthConstants.ClaimUserId).Should().Be("user-123");
        principal.FindFirstValue(AuthConstants.ClaimEmail).Should().Be("tester@oaza.cz");
        principal.FindFirstValue(AuthConstants.ClaimRole).Should().Be("Member");
        principal.FindFirstValue(AuthConstants.ClaimHouseId).Should().Be("house-9");
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        var service = CreateService();
        var token = service.GenerateToken(new User
        {
            Id = "user-1",
            Email = "a@b.cz",
            Role = UserRole.Admin,
            AuthMethod = AuthMethod.MagicLink,
        });

        var tampered = token[..^2] + (token[^1] == 'a' ? "bb" : "aa");

        service.ValidateToken(tampered).Should().BeNull();
    }
}
