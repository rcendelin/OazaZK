using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Tests;

/// <summary>
/// T01: two environments configured like test and prod (each its own storage) do not see each other's data. Needs two
/// storage emulators — the default Azurite and a second one whose connection string is in
/// <c>AZURE_STORAGE_CONNECTION_2</c> (CI runs a second Azurite on ports 20000–20002); skipped otherwise.
/// </summary>
[Collection("Azurite")]
public class EnvironmentIsolationIntegrationTests
{
    private readonly AzuriteFixture _fx;

    public EnvironmentIsolationIntegrationTests(AzuriteFixture fx) => _fx = fx;

    private static ServiceProvider Environment(string connection, string? environment = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["TableStorageConnection"] = connection,
            ["BlobStorageConnection"] = connection,
            ["Environment"] = environment,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
    }

    [SkippableFact]
    public async Task AWriteInOneEnvironment_IsNotVisibleInTheOther()
    {
        Skip.IfNot(_fx.Available, "Azurite emulator not available on the Table endpoint.");
        var second = System.Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_2");
        Skip.If(string.IsNullOrWhiteSpace(second), "AZURE_STORAGE_CONNECTION_2 (second storage emulator) is not set.");

        using var test = Environment(AzuriteFixture.ConnectionString);
        using var prod = Environment(second!);
        var houseId = "isolation-" + Guid.NewGuid().ToString("N");

        await test.GetRequiredService<IHouseRepository>().UpsertAsync(new House { Id = houseId, Name = "Jen v testu" });

        (await test.GetRequiredService<IHouseRepository>().GetAsync(PartitionKeys.House, houseId)).Should().NotBeNull();
        (await prod.GetRequiredService<IHouseRepository>().GetAsync(PartitionKeys.House, houseId)).Should().BeNull();

        await prod.GetRequiredService<IHouseRepository>().UpsertAsync(new House { Id = houseId, Name = "Jen v produkci" });
        (await test.GetRequiredService<IHouseRepository>().GetAsync(PartitionKeys.House, houseId))!.Name.Should().Be("Jen v testu");
    }

    [Fact]
    public void ProdOnTheEmulatorStorage_DoesNotStart() =>
        FluentActions.Invoking(() => Environment(AzuriteFixture.ConnectionString, "prod"))
            .Should().Throw<InvalidOperationException>().WithMessage("Produkce nesmí*devstoreaccount1*");
}
