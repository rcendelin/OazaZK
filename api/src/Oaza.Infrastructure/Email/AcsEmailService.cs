using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oaza.Application.Interfaces;

namespace Oaza.Infrastructure.Email;

public class AcsEmailService : IEmailService
{
    private readonly AcsSettings _settings;
    private readonly ILogger<AcsEmailService> _logger;

    public AcsEmailService(
        IOptions<AcsSettings> settings,
        ILogger<AcsEmailService> logger)
    {
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendMagicLinkAsync(string toEmail, string toName, string magicLinkUrl)
    {
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
                <h2 style="color: #4f46e5;">Přihlášení do portálu Oáza</h2>
                <p>Dobrý den {System.Net.WebUtility.HtmlEncode(toName)},</p>
                <p>pro přihlášení do portálu Oáza Zadní Kopanina klikněte na následující tlačítko:</p>
                <p style="text-align: center; margin: 32px 0;">
                    <a href="{System.Net.WebUtility.HtmlEncode(magicLinkUrl)}"
                       style="background-color: #4f46e5; color: white; padding: 12px 32px;
                              text-decoration: none; border-radius: 6px; font-weight: bold;
                              display: inline-block;">
                        Přihlásit se
                    </a>
                </p>
                <p style="color: #71717a; font-size: 14px;">
                    Odkaz je platný 15 minut a lze jej použít pouze jednou.
                </p>
                <p style="color: #71717a; font-size: 14px;">
                    Pokud jste o přihlášení nežádali, tento email můžete ignorovat.
                </p>
                <hr style="border: none; border-top: 1px solid #e4e4e7; margin: 24px 0;" />
                <p style="color: #a1a1aa; font-size: 12px;">
                    Portál Oáza Zadní Kopanina
                </p>
            </div>
            """;

        await SendEmailAsync(toEmail, toName, subject, plainTextContent, htmlContent);
    }

    public async Task SendEmailAsync(string toEmail, string toName, string subject, string plainTextContent, string htmlContent)
    {
        if (string.IsNullOrWhiteSpace(_settings.ConnectionString))
        {
            _logger.LogWarning("Azure Communication Services connection string is not configured. Email to {Email} will not be sent.", toEmail);
            throw new InvalidOperationException("Azure Communication Services connection string is not configured.");
        }

        var client = new EmailClient(_settings.ConnectionString);

        var emailMessage = new EmailMessage(
            senderAddress: _settings.FromEmail,
            content: new EmailContent(subject)
            {
                PlainText = plainTextContent,
                Html = htmlContent,
            },
            recipients: new EmailRecipients(
                new List<EmailAddress> { new(toEmail, toName) }
            )
        );

        try
        {
            EmailSendOperation operation = await client.SendAsync(WaitUntil.Completed, emailMessage);

            if (operation.Value.Status == EmailSendStatus.Succeeded)
            {
                _logger.LogInformation("Email sent successfully to {Email}. Subject: {Subject}", toEmail, subject);
            }
            else
            {
                _logger.LogError(
                    "Azure Communication Services returned status {Status} when sending email to {Email}. Subject: {Subject}",
                    operation.Value.Status, toEmail, subject);
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Azure Communication Services failed to send email to {Email}. Subject: {Subject}. Error: {ErrorCode}",
                toEmail, subject, ex.ErrorCode);
            throw new InvalidOperationException(
                $"Failed to send email via Azure Communication Services. Error: {ex.ErrorCode}", ex);
        }
    }
}
