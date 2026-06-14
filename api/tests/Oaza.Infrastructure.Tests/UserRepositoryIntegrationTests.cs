using FluentAssertions;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Enums;
using Oaza.Infrastructure.Persistence;

namespace Oaza.Infrastructure.Tests;

[Collection("Azurite")]
public class UserRepositoryIntegrationTests
{
    private readonly AzuriteFixture _fx;

    public UserRepositoryIntegrationTests(AzuriteFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Upsert_Get_RoundTripsAllMappedFields_AndLookupByEmail()
    {
        Skip.IfNot(_fx.Available, "Azurite emulator not available on the Table endpoint.");
        var repo = new UserRepository(_fx.ServiceClient);
        var id = Guid.NewGuid().ToString();
        var email = $"user-{id}@oaza.cz";

        var user = new User
        {
            Id = id,
            Name = "Integration Tester",
            Email = email,
            Role = UserRole.Accountant,
            HouseId = "house-7",
            AuthMethod = AuthMethod.MagicLink,
            MagicLinkTokenHash = "hashed-token-value",
            MagicLinkExpiry = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            NotificationsEnabled = false,
            MagicLinkRequestCount = 2,
            MagicLinkFailedAttempts = 1,
        };

        try
        {
            await repo.UpsertAsync(user);

            var loaded = await repo.GetAsync(PartitionKeys.User, id);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Integration Tester");
            loaded.Email.Should().Be(email);
            loaded.Role.Should().Be(UserRole.Accountant);
            loaded.HouseId.Should().Be("house-7");
            loaded.AuthMethod.Should().Be(AuthMethod.MagicLink);
            loaded.MagicLinkTokenHash.Should().Be("hashed-token-value");
            loaded.MagicLinkExpiry.Should().Be(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            loaded.NotificationsEnabled.Should().BeFalse();
            loaded.MagicLinkRequestCount.Should().Be(2);
            loaded.MagicLinkFailedAttempts.Should().Be(1);

            var byEmail = await repo.GetByEmailAsync(email.ToUpperInvariant());
            byEmail.Should().NotBeNull("email lookup is case-insensitive");
            byEmail!.Id.Should().Be(id);
        }
        finally
        {
            await repo.DeleteAsync(PartitionKeys.User, id);
        }
    }
}
