namespace Oaza.Application.Interfaces;

public interface IEmailService
{
    Task SendMagicLinkAsync(string toEmail, string toName, string magicLinkUrl);
    Task SendEmailAsync(string toEmail, string toName, string subject, string plainTextContent, string htmlContent);
}
