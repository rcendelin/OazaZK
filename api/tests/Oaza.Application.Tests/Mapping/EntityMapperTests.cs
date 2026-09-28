using FluentAssertions;
using Oaza.Application.Mapping;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;

namespace Oaza.Application.Tests.Mapping;

public class EntityMapperTests
{
    [Fact]
    public void ToResponse_User_ExcludesMagicLinkTokenHash()
    {
        var user = new User
        {
            Id = "user-1",
            Name = "Test User",
            Email = "test@example.com",
            Role = UserRole.Admin,
            HouseId = "house-1",
            AuthMethod = AuthMethod.MagicLink,
            MagicLinkTokenHash = "secret-token-hash",
            MagicLinkExpiry = DateTime.UtcNow.AddMinutes(15),
            LastLogin = DateTime.UtcNow,
            NotificationsEnabled = true,
        };

        var response = EntityMapper.ToResponse(user);

        response.Id.Should().Be("user-1");
        response.Name.Should().Be("Test User");
        response.Email.Should().Be("test@example.com");
        response.Role.Should().Be("Admin");
        response.HouseId.Should().Be("house-1");
        response.AuthMethod.Should().Be("MagicLink");
        response.LastLogin.Should().NotBeNull();
        response.NotificationsEnabled.Should().BeTrue();

        // The response type should NOT have MagicLinkToken or EntraObjectId properties.
        // Verify by checking that the response object does not expose sensitive fields.
        var responseType = response.GetType();
        responseType.GetProperty("MagicLinkToken").Should().BeNull(
            "UserResponse must not expose MagicLinkToken");
        responseType.GetProperty("MagicLinkTokenHash").Should().BeNull(
            "UserResponse must not expose MagicLinkTokenHash");
        responseType.GetProperty("EntraObjectId").Should().BeNull(
            "UserResponse must not expose EntraObjectId");
        responseType.GetProperty("MagicLinkExpiry").Should().BeNull(
            "UserResponse must not expose MagicLinkExpiry");
    }

    [Fact]
    public void ToResponse_User_ExcludesEntraObjectId()
    {
        var user = new User
        {
            Id = "user-2",
            Name = "Entra User",
            Email = "entra@example.com",
            Role = UserRole.Member,
            HouseId = null,
            AuthMethod = AuthMethod.EntraId,
            EntraObjectId = "entra-object-id-12345",
            LastLogin = null,
            NotificationsEnabled = false,
        };

        var response = EntityMapper.ToResponse(user);

        response.Id.Should().Be("user-2");
        response.Role.Should().Be("Member");
        response.HouseId.Should().BeNull();
        response.AuthMethod.Should().Be("EntraId");
        response.LastLogin.Should().BeNull();
        response.NotificationsEnabled.Should().BeFalse();

        // EntraObjectId must not be in the response
        var responseType = response.GetType();
        responseType.GetProperty("EntraObjectId").Should().BeNull();
    }

    [Fact]
    public void ToResponse_House_MapsAllProperties()
    {
        var house = new House
        {
            Id = "house-1",
            Name = "Novákovi (142)",
            Address = "Zadní Kopanina 142",
            ContactPerson = "Jan Novák",
            Email = "novak@example.com",
            IsActive = true,
        };

        var response = EntityMapper.ToResponse(house);

        response.Id.Should().Be("house-1");
        response.Name.Should().Be("Novákovi (142)");
        response.Address.Should().Be("Zadní Kopanina 142");
        response.ContactPerson.Should().Be("Jan Novák");
        response.Email.Should().Be("novak@example.com");
        response.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ToResponse_House_InactiveHouse()
    {
        var house = new House
        {
            Id = "house-2",
            Name = "Inactive House",
            Address = "Some Address",
            ContactPerson = "Nobody",
            Email = "inactive@example.com",
            IsActive = false,
        };

        var response = EntityMapper.ToResponse(house);

        response.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ToResponse_WaterMeter_MapsTypeAsString()
    {
        var meter = new WaterMeter
        {
            Id = "meter-1",
            MeterNumber = "WM-001",
            Type = MeterType.Individual,
            HouseId = "house-1",
            InstallationDate = new DateTime(2020, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        };

        var response = EntityMapper.ToResponse(meter);

        response.Id.Should().Be("meter-1");
        response.MeterNumber.Should().Be("WM-001");
        response.Type.Should().Be("Individual");
        response.HouseId.Should().Be("house-1");
    }

    [Fact]
    public void ToResponse_WaterMeter_MainMeter_NullHouseId()
    {
        var meter = new WaterMeter
        {
            Id = "main-meter",
            MeterNumber = "MAIN-001",
            Type = MeterType.Main,
            HouseId = null,
            InstallationDate = new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var response = EntityMapper.ToResponse(meter);

        response.Type.Should().Be("Main");
        response.HouseId.Should().BeNull();
    }

    [Fact]
    public void ToResponse_AdvancePayment_IncludesHouseName()
    {
        var payment = new AdvancePayment
        {
            HouseId = "house-1",
            Year = 2025,
            Month = 3,
            Amount = 1500m,
            PaymentDate = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc),
        };

        var response = EntityMapper.ToResponse(payment, "Novákovi (142)");

        response.HouseId.Should().Be("house-1");
        response.HouseName.Should().Be("Novákovi (142)");
        response.Year.Should().Be(2025);
        response.Month.Should().Be(3);
        response.Amount.Should().Be(1500m);
    }

    [Fact]
    public void ToResponse_AdvancePayment_NullHouseName()
    {
        var payment = new AdvancePayment
        {
            HouseId = "house-1",
            Year = 2025,
            Month = 1,
            Amount = 500m,
            PaymentDate = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        };

        var response = EntityMapper.ToResponse(payment);

        response.HouseName.Should().BeNull();
    }

}
