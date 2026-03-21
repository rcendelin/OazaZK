using Oaza.Application.DTOs;
using Oaza.Domain.Entities;

namespace Oaza.Application.Mapping;

public static class EntityMapper
{
    public static HouseResponse ToResponse(House house)
    {
        return new HouseResponse
        {
            Id = house.Id,
            Name = house.Name,
            Address = house.Address,
            ContactPerson = house.ContactPerson,
            Email = house.Email,
            IsActive = house.IsActive,
        };
    }

    public static MeterResponse ToResponse(WaterMeter meter)
    {
        return new MeterResponse
        {
            Id = meter.Id,
            MeterNumber = meter.MeterNumber,
            Type = meter.Type.ToString(),
            HouseId = meter.HouseId,
            InstallationDate = meter.InstallationDate,
        };
    }

    // IMPORTANT: Never include MagicLinkTokenHash or EntraObjectId in response
    public static UserResponse ToResponse(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role.ToString(),
            HouseId = user.HouseId,
            AuthMethod = user.AuthMethod.ToString(),
            LastLogin = user.LastLogin,
            NotificationsEnabled = user.NotificationsEnabled,
        };
    }
}
