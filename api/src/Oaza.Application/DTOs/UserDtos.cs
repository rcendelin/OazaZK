namespace Oaza.Application.DTOs;

public class CreateUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // "Admin", "Member", "Accountant"
    public string? HouseId { get; set; }
    public string AuthMethod { get; set; } = string.Empty; // "EntraId", "MagicLink"
}

public class UpdateUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? HouseId { get; set; }
    public bool? NotificationsEnabled { get; set; }
}

public class UserResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? HouseId { get; set; }
    public string AuthMethod { get; set; } = string.Empty;
    public DateTime? LastLogin { get; set; }
    public bool NotificationsEnabled { get; set; }
}
