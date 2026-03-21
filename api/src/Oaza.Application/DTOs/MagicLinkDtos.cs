namespace Oaza.Application.DTOs;

public record MagicLinkRequest(string Email);

public record MagicLinkVerifyRequest(string Token, string Email);

public record AuthResponse(string Token, DateTime ExpiresAt);
