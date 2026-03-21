using Oaza.Domain.Enums;

namespace Oaza.Domain.Entities;

public class User
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? HouseId { get; set; }
    public AuthMethod AuthMethod { get; set; }
    public string? EntraObjectId { get; set; }
    public string? MagicLinkTokenHash { get; set; }
    public DateTime? MagicLinkExpiry { get; set; }
    public DateTime? LastLogin { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
}
