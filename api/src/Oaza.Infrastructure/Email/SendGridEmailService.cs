using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oaza.Application.Interfaces;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Oaza.Infrastructure.Email;

public class SendGridEmailService : IEmailService
{
    private readonly SendGridSettings _settings;
    private readonly ILogger<SendGridEmailService> _logger;

    public SendGridEmailService(
        IOptions<SendGridSettings> settings,
        ILogger<SendGridEmailService> logger)
    {
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendMagicLinkAsync(string toEmail, string toName, string magicLinkUrl)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning("SendGrid API key is not configured. Magic link email will not be sent.");
            return;
        }

        var client = new SendGridClient(_settings.ApiKey);
        var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
        var to = new EmailAddress(toEmail, toName);

        var subject = "Přihlášení do portálu Oáza";

        var plainTextContent = $"""
            Dobrý den {toName},

            pro přihlášení do portálu Oáza Zadní Kopanina klikněte na následující odkaz:

            {magicLinkUrl}

            Odkaz je platný 15 minut a lze jej použít pouze jednou.

            Pokud jste o přihlášení nežádali, tento email můžete ignorovat.

            S pozdravem,
            Portál Oáza Zadní Kopanina
            """;

        var htmlContent = $"""
            <div style="font-family: sans-serif; max-width: 600px; margin: 0 auto;">
                <h2 style="color: #2563eb;">Přihlášení do portálu Oáza</h2>
                <p>Dobrý den {System.Net.WebUtility.HtmlEncode(toName)},</p>
                <p>pro přihlášení do portálu Oáza Zadní Kopanina klikněte na následující tlačítko:</p>
                <p style="text-align: center; margin: 32px 0;">
                    <a href="{System.Net.WebUtility.HtmlEncode(magicLinkUrl)}"
                       style="background-color: #2563eb; color: white; padding: 12px 32px;
                              text-decoration: none; border-radius: 6px; font-weight: bold;
                              display: inline-block;">
                        Přihlásit se
                    </a>
                </p>
                <p style="color: #6b7280; font-size: 14px;">
                    Odkaz je platný 15 minut a lze jej použít pouze jednou.
                </p>
                <p style="color: #6b7280; font-size: 14px;">
                    Pokud jste o přihlášení nežádali, tento email můžete ignorovat.
                </p>
                <hr style="border: none; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
                <p style="color: #9ca3af; font-size: 12px;">
                    Portál Oáza Zadní Kopanina
                </p>
            </div>
            """;

        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);

        var response = await client.SendEmailAsync(msg);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Magic link email sent successfully to {Email}.", toEmail);
        }
        else
        {
            var body = await response.Body.ReadAsStringAsync();
            _logger.LogError(
                "SendGrid returned {StatusCode} when sending magic link email to {Email}. Response: {Body}",
                response.StatusCode, toEmail, body);
            throw new InvalidOperationException(
                $"Failed to send magic link email. SendGrid status: {response.StatusCode}");
        }
    }

    public async Task SendEmailAsync(string toEmail, string toName, string subject, string plainTextContent, string htmlContent)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning("SendGrid API key is not configured. Email will not be sent.");
            return;
        }

        var client = new SendGridClient(_settings.ApiKey);
        var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
        var to = new EmailAddress(toEmail, toName);

        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);

        var response = await client.SendEmailAsync(msg);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Email sent successfully to {Email}. Subject: {Subject}", toEmail, subject);
        }
        else
        {
            var body = await response.Body.ReadAsStringAsync();
            _logger.LogError(
                "SendGrid returned {StatusCode} when sending email to {Email}. Subject: {Subject}. Response: {Body}",
                response.StatusCode, toEmail, subject, body);
        }
    }
}
