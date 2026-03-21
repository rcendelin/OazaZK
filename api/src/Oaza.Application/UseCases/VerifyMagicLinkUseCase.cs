using Microsoft.Extensions.Logging;
using Oaza.Application.Auth;
using Oaza.Application.DTOs;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.UseCases;

public class VerifyMagicLinkUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;
    private readonly ILogger<VerifyMagicLinkUseCase> _logger;
    private readonly int _jwtExpiryHours;

    public VerifyMagicLinkUseCase(
        IUserRepository userRepository,
        IJwtService jwtService,
        ILogger<VerifyMagicLinkUseCase> logger,
        int jwtExpiryHours = 24)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jwtExpiryHours = jwtExpiryHours;
    }

    /// <summary>
    /// Verifies a magic link token and returns a JWT if valid.
    /// Returns null if the token is invalid or expired.
    /// </summary>
    public async Task<AuthResponse?> ExecuteAsync(string token, string email)
    {
        var user = await _userRepository.GetByEmailAsync(email);
        if (user is null)
        {
            _logger.LogWarning("Magic link verification attempted for unknown email.");
            return null;
        }

        // Check failed attempts
        if (user.MagicLinkFailedAttempts >= 5)
        {
            user.MagicLinkTokenHash = null;
            user.MagicLinkExpiry = null;
            user.MagicLinkFailedAttempts = 0;
            await _userRepository.UpsertAsync(user);
            return null;
        }

        // Check if there is a stored token hash
        if (string.IsNullOrEmpty(user.MagicLinkTokenHash))
        {
            _logger.LogWarning("Magic link verification attempted but no token is stored for user {UserId}.", user.Id);
            return null;
        }

        // Check expiry
        if (!user.MagicLinkExpiry.HasValue || user.MagicLinkExpiry.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("Magic link token expired for user {UserId}.", user.Id);
            // Clear the expired token
            user.MagicLinkTokenHash = null;
            user.MagicLinkExpiry = null;
            await _userRepository.UpsertAsync(user);
            return null;
        }

        // Hash the incoming token and compare with stored hash
        var incomingHash = TokenHasher.Hash(token);
        if (!string.Equals(incomingHash, user.MagicLinkTokenHash, StringComparison.Ordinal))
        {
            _logger.LogWarning("Magic link token hash mismatch for user {UserId}.", user.Id);
            user.MagicLinkFailedAttempts++;
            await _userRepository.UpsertAsync(user);
            return null;
        }

        // Token is valid — clear it (one-time use) and update last login
        user.MagicLinkTokenHash = null;
        user.MagicLinkExpiry = null;
        user.MagicLinkFailedAttempts = 0;
        user.LastLogin = DateTime.UtcNow;

        await _userRepository.UpsertAsync(user);

        // Generate JWT
        var jwt = _jwtService.GenerateToken(user);
        var expiresAt = DateTime.UtcNow.AddHours(_jwtExpiryHours);

        _logger.LogInformation("Magic link verified successfully for user {UserId}.", user.Id);

        return new AuthResponse(jwt, expiresAt);
    }
}
