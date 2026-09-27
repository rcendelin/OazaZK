using Azure.Data.Tables;
using Azure.Storage.Blobs;
using FluentAssertions;
using Oaza.Infrastructure.Backup;

namespace Oaza.Infrastructure.Tests;

[Collection("Azurite")]
public class StorageBackupIntegrationTests
{
    private readonly AzuriteFixture _fx;

    public StorageBackupIntegrationTests(AzuriteFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task BackupThenRestore_BringsBackTablesAndBlobsExactly_WithTheirTypes()
    {
        Skip.IfNot(_fx.Available, "Azurite emulator not available on the Table endpoint.");
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tableName = $"Bkp{suffix}";
        var containerName = $"bkp{suffix}";
        var backup = new StorageBackup(AzuriteFixture.ConnectionString, name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var folder = Path.Combine(Path.GetTempPath(), $"oaza-backup-{suffix}");

        var table = _fx.ServiceClient.GetTableClient(tableName);
        await table.CreateAsync();
        var original = new TableEntity("P", "1")
        {
            ["Text"] = "Zadní Kopanina", ["Flag"] = true, ["Small"] = 42, ["Big"] = 9_000_000_000L, ["Real"] = 1.5,
            ["When"] = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero), ["Id"] = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ["Bytes"] = new byte[] { 1, 2, 3 },
        };
        await table.AddEntityAsync(original);
        await table.AddEntityAsync(new TableEntity("P", "2") { ["Text"] = "druhý" });
        var blobs = new BlobServiceClient(AzuriteFixture.ConnectionString).GetBlobContainerClient(containerName);
        await blobs.CreateAsync();
        await blobs.GetBlobClient("dum/faktura.pdf").UploadAsync(BinaryData.FromString("PDF"));

        var saved = await backup.BackupAsync(folder);

        // Damage: change one entity, delete one, add an extra entity and blob, overwrite the blob.
        await table.UpsertEntityAsync(new TableEntity("P", "1") { ["Text"] = "změněno" }, TableUpdateMode.Replace);
        await table.DeleteEntityAsync("P", "2");
        await table.AddEntityAsync(new TableEntity("P", "3") { ["Text"] = "navíc" });
        await blobs.GetBlobClient("dum/faktura.pdf").UploadAsync(BinaryData.FromString("jiné"), overwrite: true);
        await blobs.GetBlobClient("navic.txt").UploadAsync(BinaryData.FromString("x"));

        var restored = await backup.RestoreAsync(folder);

        saved.Tables.Should().Equal(new Dictionary<string, int> { [tableName] = 2 });
        saved.Containers.Should().Equal(new Dictionary<string, int> { [containerName] = 1 });
        restored.Deleted.Should().Be(2);
        var rows = table.Query<TableEntity>().OrderBy(e => e.RowKey).ToList();
        rows.Select(e => e.RowKey).Should().Equal("1", "2");
        var first = rows[0];
        first.GetString("Text").Should().Be("Zadní Kopanina");
        first.GetBoolean("Flag").Should().BeTrue();
        first.GetInt32("Small").Should().Be(42);
        first.GetInt64("Big").Should().Be(9_000_000_000L);
        first.GetDouble("Real").Should().Be(1.5);
        first.GetDateTimeOffset("When").Should().Be(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero));
        first.GetGuid("Id").Should().Be(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        first.GetBinary("Bytes").Should().Equal(1, 2, 3);
        blobs.GetBlobs().Select(b => b.Name).Should().Equal("dum/faktura.pdf");
        (await blobs.GetBlobClient("dum/faktura.pdf").DownloadContentAsync()).Value.Content.ToString().Should().Be("PDF");

        await table.DeleteAsync();
        await blobs.DeleteAsync();
        Directory.Delete(folder, recursive: true);
    }

    [Theory]
    [InlineData("azure-webjobs-secrets", true)]
    [InlineData("azure-webjobs-hosts", true)]
    [InlineData("function-releases", true)]
    [InlineData("scm-releases", true)]
    [InlineData("AzureFunctionsDiagnosticEvents202609", true)]
    [InlineData("AzureWebJobsHostLogs202609", true)]
    [InlineData("documents", false)]
    [InlineData("finance", false)]
    [InlineData("Houses", false)]
    public void HostData_IsNeverBackedUp(string name, bool hostManaged) =>
        StorageBackup.IsHostManaged(name).Should().Be(hostManaged);

    [Fact]
    public void UnknownType_IsRejected()
    {
        var entity = new TableEntity("P", "1") { ["Odd"] = new Uri("https://example.invalid") };

        FluentActions.Invoking(() => StorageBackup.Serialize(entity)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => StorageBackup.Deserialize(System.Text.Json.Nodes.JsonNode.Parse("""{"pk":"P","rk":"1","p":{"X":{"t":"Decimal","v":"1"}}}""")!.AsObject()))
            .Should().Throw<NotSupportedException>();
    }
}
