namespace Oaza.Infrastructure.Auth;

public class EntraIdSettings
{
    public const string SectionName = "EntraId";

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}
