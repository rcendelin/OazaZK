using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Oaza.Application.Auth;
using Oaza.Application.UseCases;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Tests.UseCases;

public class VerifyMagicLinkUseCaseTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IJwtService> _jwtServiceMock;
    private readonly Mock<ILogger<VerifyMagicLinkUseCase>> _loggerMock;
    private readonly VerifyMagicLinkUseCase _sut;

    public VerifyMagicLinkUseCaseTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _jwtServiceMock = new Mock<IJwtService>();
        _loggerMock = new Mock<ILogger<VerifyMagicLinkUseCase>>();
        _sut = new VerifyMagicLinkUseCase(
            _userRepoMock.Object,
            _jwtServiceMock.Object,
            _loggerMock.Object);
    }

    private static (User user, string rawToken) CreateUserWithValidToken()
    {
        var rawToken = Guid.NewGuid().ToString();
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test User",
            Email = "test@example.com",
            Role = UserRole.Member,
            AuthMethod = AuthMethod.MagicLink,
            MagicLinkTokenHash = TokenHasher.Hash(rawToken),
            MagicLinkExpiry = DateTime.UtcNow.AddMinutes(10),
        };
        return (user, rawToken);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownEmail_ReturnsNull()
    {
        _userRepoMock.Setup(r => r.GetByEmailAsync("unknown@example.com"))
            .ReturnsAsync((User?)null);

        var result = await _sut.ExecuteAsync("some-token", "unknown@example.com");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NoStoredToken_ReturnsNull()
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "test@example.com",
            MagicLinkTokenHash = null,
            MagicLinkExpiry = null,
        };
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var result = await _sut.ExecuteAsync("some-token", user.Email);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredToken_ReturnsNullAndClearsToken()
    {
        var rawToken = Guid.NewGuid().ToString();
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = "test@example.com",
            MagicLinkTokenHash = TokenHasher.Hash(rawToken),
            MagicLinkExpiry = DateTime.UtcNow.AddMinutes(-1), // expired
        };
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var result = await _sut.ExecuteAsync(rawToken, user.Email);

        result.Should().BeNull();
        // Token should be cleared
        _userRepoMock.Verify(r => r.UpsertAsync(It.Is<User>(u =>
            u.MagicLinkTokenHash == null && u.MagicLinkExpiry == null)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WrongToken_ReturnsNull()
    {
        var (user, _) = CreateUserWithValidToken();
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);

        var result = await _sut.ExecuteAsync("wrong-token", user.Email);

        result.Should().BeNull();
        // Failed attempt should be tracked
        _userRepoMock.Verify(r => r.UpsertAsync(It.Is<User>(u =>
            u.MagicLinkFailedAttempts == 1)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ValidToken_ReturnsJwtAndClearsToken()
    {
        var (user, rawToken) = CreateUserWithValidToken();
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _jwtServiceMock.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("jwt-token-123");

        var result = await _sut.ExecuteAsync(rawToken, user.Email);

        result.Should().NotBeNull();
        result!.Token.Should().Be("jwt-token-123");
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        // Token should be cleared (one-time use)
        _userRepoMock.Verify(r => r.UpsertAsync(It.Is<User>(u =>
            u.MagicLinkTokenHash == null &&
            u.MagicLinkExpiry == null &&
            u.LastLogin != null)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ValidToken_UpdatesLastLogin()
    {
        var (user, rawToken) = CreateUserWithValidToken();
        User? savedUser = null;
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.UpsertAsync(It.IsAny<User>()))
            .Callback<User>(u => savedUser = u)
            .Returns(Task.CompletedTask);
        _jwtServiceMock.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("jwt");

        var before = DateTime.UtcNow;
        await _sut.ExecuteAsync(rawToken, user.Email);
        var after = DateTime.UtcNow;

        savedUser.Should().NotBeNull();
        savedUser!.LastLogin.Should().BeOnOrAfter(before);
        savedUser.LastLogin.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task ExecuteAsync_TokenUsedTwice_SecondAttemptFails()
    {
        var (user, rawToken) = CreateUserWithValidToken();
        _userRepoMock.Setup(r => r.GetByEmailAsync(user.Email)).ReturnsAsync(user);
        _jwtServiceMock.Setup(j => j.GenerateToken(It.IsAny<User>())).Returns("jwt");

        // First verification succeeds and clears the token
        var result1 = await _sut.ExecuteAsync(rawToken, user.Email);
        result1.Should().NotBeNull();

        // After first call, token fields are cleared on the user object
        user.MagicLinkTokenHash.Should().BeNull();
        user.MagicLinkExpiry.Should().BeNull();

        // Second call with same token fails because token was cleared
        var result2 = await _sut.ExecuteAsync(rawToken, user.Email);
        result2.Should().BeNull();
    }
}
