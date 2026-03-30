using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oaza.Application.Interfaces;
using Oaza.Application.UseCases;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class RequestMagicLinkUseCaseTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<ILogger<RequestMagicLinkUseCase>> _loggerMock;
    private readonly RequestMagicLinkUseCase _sut;

    public RequestMagicLinkUseCaseTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        _loggerMock = new Mock<ILogger<RequestMagicLinkUseCase>>();
        _sut = new RequestMagicLinkUseCase(
            _userRepoMock.Object,
            _emailServiceMock.Object,
            _loggerMock.Object,
            "https://oaza.cendelinovi.cz");
    }

    private static User CreateTestUser(string email = "test@example.com") => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = "Test User",
        Email = email,
        Role = UserRole.Member,
        AuthMethod = AuthMethod.MagicLink,
    };

    [Fact]
    public async Task ExecuteAsync_UnknownEmail_DoesNotSendEmail()
    {
        _userRepoMock.Setup(r => r.GetByEmailAsync("unknown@example.com"))
            .ReturnsAsync((User?)null);

        await _sut.ExecuteAsync("unknown@example.com");

        _emailServiceMock.Verify(
            e => e.SendMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _userRepoMock.Verify(r => r.UpsertAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ValidEmail_StoresHashedTokenAndSendsEmail()
    {
        var user = CreateTestUser();
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        await _sut.ExecuteAsync(user.Email);

        // Verify token hash was stored (not raw token)
        _userRepoMock.Verify(r => r.UpsertAsync(It.Is<User>(u =>
            u.MagicLinkTokenHash != null &&
            u.MagicLinkExpiry != null &&
            u.MagicLinkExpiry > DateTime.UtcNow)), Times.Once);

        // Verify email was sent
        _emailServiceMock.Verify(
            e => e.SendMagicLinkAsync(
                user.Email,
                user.Name,
                It.Is<string>(url => url.Contains("/auth/verify?token=") && url.Contains("email="))),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ValidEmail_TokenHashIsSha256NotRawGuid()
    {
        var user = CreateTestUser();
        User? savedUser = null;
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.UpsertAsync(It.IsAny<User>()))
            .Callback<User>(u => savedUser = u)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(user.Email);

        savedUser.Should().NotBeNull();
        savedUser!.MagicLinkTokenHash.Should().NotBeNullOrEmpty();

        // The stored hash should not be a valid GUID (it's a base64 SHA-256 hash)
        Guid.TryParse(savedUser.MagicLinkTokenHash, out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ExpiryIsSet15MinutesInFuture()
    {
        var user = CreateTestUser();
        User? savedUser = null;
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.UpsertAsync(It.IsAny<User>()))
            .Callback<User>(u => savedUser = u)
            .Returns(Task.CompletedTask);

        var beforeExec = DateTime.UtcNow;
        await _sut.ExecuteAsync(user.Email);
        var afterExec = DateTime.UtcNow;

        savedUser.Should().NotBeNull();
        savedUser!.MagicLinkExpiry.Should().BeAfter(beforeExec.AddMinutes(14));
        savedUser.MagicLinkExpiry.Should().BeBefore(afterExec.AddMinutes(16));
    }

    [Fact]
    public async Task ExecuteAsync_RecentToken_DoesNotGenerateNewToken()
    {
        var user = CreateTestUser();
        // Rate limit: 3 requests already made within the current window
        user.MagicLinkRequestWindowStart = DateTime.UtcNow.AddMinutes(-30);
        user.MagicLinkRequestCount = 3;

        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        await _sut.ExecuteAsync(user.Email);

        _userRepoMock.Verify(r => r.UpsertAsync(It.IsAny<User>()), Times.Never);
        _emailServiceMock.Verify(
            e => e.SendMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_OldToken_GeneratesNewToken()
    {
        var user = CreateTestUser();
        // Token created 10 minutes ago (expiry is 5 minutes from now)
        user.MagicLinkTokenHash = "old-hash";
        user.MagicLinkExpiry = DateTime.UtcNow.AddMinutes(5);

        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        await _sut.ExecuteAsync(user.Email);

        _userRepoMock.Verify(r => r.UpsertAsync(It.IsAny<User>()), Times.Once);
        _emailServiceMock.Verify(
            e => e.SendMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EmailServiceThrows_DoesNotCrash()
    {
        var user = CreateTestUser();
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _emailServiceMock.Setup(e => e.SendMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Email service error"));

        // Should not throw
        await _sut.ExecuteAsync(user.Email);

        // Token should still be saved even if email fails
        _userRepoMock.Verify(r => r.UpsertAsync(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MagicLinkUrl_ContainsUrlEncodedEmail()
    {
        var user = CreateTestUser("user+test@example.com");
        string? sentUrl = null;
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _emailServiceMock.Setup(e => e.SendMagicLinkAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, url) => sentUrl = url)
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(user.Email);

        sentUrl.Should().NotBeNull();
        sentUrl.Should().Contain("email=user%2Btest%40example.com");
        sentUrl.Should().StartWith("https://oaza.cendelinovi.cz/auth/verify?token=");
    }
}
