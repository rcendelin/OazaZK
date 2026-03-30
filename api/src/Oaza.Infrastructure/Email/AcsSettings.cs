namespace Oaza.Infrastructure.Email;

public class AcsSettings
{
    public const string SectionName = "AzureCommunicationServices";

    public string ConnectionString { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
}
