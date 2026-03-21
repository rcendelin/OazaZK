using Microsoft.Extensions.Logging;
using Oaza.Application.Interfaces;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

public class RequestMagicLinkUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly ILogger<RequestMagicLinkUseCase> _logger;
    private readonly string _appUrl;

    public RequestMagicLinkUseCase(
        IUserRepository userRepository,
        IEmailService emailService,
        ILogger<RequestMagicLinkUseCase> logger,
        string appUrl)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _appUrl = appUrl ?? throw new ArgumentNullException(nameof(appUrl));
    }

    /// <summary>
    /// Generates a magic link token and sends it via email.
    /// Always succeeds from the caller's perspective to prevent email enumeration.
    /// </summary>
    public async Task ExecuteAsync(string email)
    {
        var user = await _userRepository.GetByEmailAsync(email);
        if (user is null)
        {
            _logger.LogInformation("Magic link requested for unregistered email. Silently ignoring.");
            return;
        }

        // Check rate limit: max 3 requests per hour
        var now = DateTime.UtcNow;
        var windowStart = user.MagicLinkRequestWindowStart;
        if (windowStart.HasValue && (now - windowStart.Value).TotalHours < 1)
        {
            if (user.MagicLinkRequestCount >= 3)
            {
                _logger.LogWarning("Rate limit exceeded for email {Email}", email);
                return; // Silent — don't reveal rate limiting
            }
            user.MagicLinkRequestCount++;
        }
        else
        {
            // New window
            user.MagicLinkRequestWindowStart = now;
            user.MagicLinkRequestCount = 1;
        }

        // Generate a raw token (GUID) — this goes in the email link
        var rawToken = Guid.NewGuid().ToString();

        // Store only the SHA-256 hash in the database
        user.MagicLinkTokenHash = TokenHasher.Hash(rawToken);
        user.MagicLinkExpiry = DateTime.UtcNow.AddMinutes(15);

        await _userRepository.UpsertAsync(user);

        // Build the magic link URL with URL-encoded email
        var encodedEmail = Uri.EscapeDataString(email);
        var magicLinkUrl = $"{_appUrl.TrimEnd('/')}/auth/verify?token={rawToken}&email={encodedEmail}";

        try
        {
            await _emailService.SendMagicLinkAsync(user.Email, user.Name, magicLinkUrl);
            _logger.LogInformation("Magic link sent to user {UserId}.", user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send magic link email to user {UserId}.", user.Id);
            // Don't rethrow — the token is saved, user can retry if email fails
        }
    }
}
