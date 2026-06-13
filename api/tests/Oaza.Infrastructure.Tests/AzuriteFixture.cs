using Azure.Data.Tables;

namespace Oaza.Infrastructure.Tests;

/// <summary>
/// Shared connection to the Azure Storage emulator (Azurite). Integration tests
/// run against a real Table endpoint to exercise the PartitionKey/RowKey strategy
/// and the TableEntityMapper round-trip. When Azurite is not reachable the tests
/// are skipped (via SkippableFact) rather than failing, so the suite stays green
/// in environments without the emulator.
/// </summary>
public sealed class AzuriteFixture
{
    // Override with a real storage connection string for cloud integration runs.
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("AZURE_TABLES_CONNECTION") ?? "UseDevelopmentStorage=true";

    public TableServiceClient ServiceClient { get; }

    public bool Available { get; }

    public AzuriteFixture()
    {
        ServiceClient = new TableServiceClient(ConnectionString);
        try
        {
            // Listing tables forces a real round-trip to the Table endpoint.
            _ = ServiceClient.Query(cancellationToken: default).Take(1).ToList();
            Available = true;
        }
        catch
        {
            Available = false;
        }
    }
}

[CollectionDefinition("Azurite")]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteFixture>;
