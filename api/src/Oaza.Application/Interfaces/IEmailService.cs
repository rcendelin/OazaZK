namespace Oaza.Application.Interfaces;

public interface IEmailService
{
    Task SendMagicLinkAsync(string toEmail, string toName, string magicLinkUrl);
}
