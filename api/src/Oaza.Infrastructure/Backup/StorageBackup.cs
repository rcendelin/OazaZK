using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Azure.Storage.Blobs;

namespace Oaza.Infrastructure.Backup;

/// <summary>Summary of a backup or restore run.</summary>
public sealed record BackupSummary(IReadOnlyDictionary<string, int> Tables, IReadOnlyDictionary<string, int> Containers, int Deleted);

/// <summary>
/// Backup and restore of a whole storage account — every table and every blob container — to a folder (release
/// checklist, T14). Table Storage has no point-in-time restore, so a release that changes data starts with a backup.
/// Format: <c>tables/{table}.jsonl</c> (one entity per line with typed properties, so a restore gives back the same
/// Edm types) and <c>blobs/{container}/{blob name}</c>. Restore replaces every backed-up entity and blob and deletes
/// entities and blobs that were not in the backup, only in the tables and containers the backup holds.
/// Data of the Functions host itself (keys, deployment packages, host locks — the app's storage account is also its
/// <c>AzureWebJobsStorage</c>) is never backed up nor restored: restoring it would roll back or break the running app.
/// </summary>
public sealed class StorageBackup
{
    private readonly TableServiceClient _tables;
    private readonly BlobServiceClient _blobs;
    private readonly Func<string, bool> _include;

    /// <param name="include">Which tables and containers to process (by name); default all app data.</param>
    public StorageBackup(string connectionString, Func<string, bool>? include = null)
    {
        _tables = new TableServiceClient(connectionString);
        _blobs = new BlobServiceClient(connectionString);
        var chosen = include ?? (_ => true);
        _include = name => !IsHostManaged(name) && chosen(name);
    }

    /// <summary>Tables and containers the Azure Functions host manages (secrets, packages, locks, timers).</summary>
    public static bool IsHostManaged(string name) =>
        name.StartsWith("azure-webjobs", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("AzureFunctions", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("AzureWebJobs", StringComparison.OrdinalIgnoreCase)
        || name.Equals("function-releases", StringComparison.OrdinalIgnoreCase)
        || name.Equals("scm-releases", StringComparison.OrdinalIgnoreCase);

    public async Task<BackupSummary> BackupAsync(string folder, CancellationToken ct = default)
    {
        var tables = new Dictionary<string, int>();
        var containers = new Dictionary<string, int>();
        Directory.CreateDirectory(Path.Combine(folder, "tables"));
        Directory.CreateDirectory(Path.Combine(folder, "blobs"));

        await foreach (var table in _tables.QueryAsync(cancellationToken: ct))
        {
            if (!_include(table.Name))
                continue;
            var count = 0;
            await using var writer = new StreamWriter(Path.Combine(folder, "tables", $"{table.Name}.jsonl"), false, new UTF8Encoding(false));
            await foreach (var entity in _tables.GetTableClient(table.Name).QueryAsync<TableEntity>(cancellationToken: ct))
            {
                await writer.WriteLineAsync(Serialize(entity).ToJsonString());
                count++;
            }
            tables[table.Name] = count;
        }

        await foreach (var item in _blobs.GetBlobContainersAsync(cancellationToken: ct))
        {
            if (!_include(item.Name))
                continue;
            var container = _blobs.GetBlobContainerClient(item.Name);
            var root = Path.Combine(folder, "blobs", item.Name);
            Directory.CreateDirectory(root);
            var count = 0;
            await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
            {
                var path = Path.Combine(root, blob.Name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await container.GetBlobClient(blob.Name).DownloadToAsync(path, ct);
                count++;
            }
            containers[item.Name] = count;
        }
        return new BackupSummary(tables, containers, 0);
    }

    public async Task<BackupSummary> RestoreAsync(string folder, CancellationToken ct = default)
    {
        var tables = new Dictionary<string, int>();
        var containers = new Dictionary<string, int>();
        var deleted = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(folder, "tables"), "*.jsonl").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!_include(name))
                continue;
            var client = _tables.GetTableClient(name);
            await client.CreateIfNotExistsAsync(ct);
            var keys = new HashSet<(string, string)>();
            foreach (var line in await File.ReadAllLinesAsync(file, ct))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var entity = Deserialize(JsonNode.Parse(line)!.AsObject());
                await client.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
                keys.Add((entity.PartitionKey, entity.RowKey));
            }
            await foreach (var existing in client.QueryAsync<TableEntity>(select: ["PartitionKey", "RowKey"], cancellationToken: ct))
            {
                if (keys.Contains((existing.PartitionKey, existing.RowKey)))
                    continue;
                await client.DeleteEntityAsync(existing.PartitionKey, existing.RowKey, cancellationToken: ct);
                deleted++;
            }
            tables[name] = keys.Count;
        }

        var blobRoot = Path.Combine(folder, "blobs");
        var directories = Directory.Exists(blobRoot) ? Directory.EnumerateDirectories(blobRoot).Order(StringComparer.Ordinal).ToList() : [];
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!_include(name))
                continue;
            var container = _blobs.GetBlobContainerClient(name);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var names = new HashSet<string>();
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var blobName = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
                await container.GetBlobClient(blobName).UploadAsync(path, overwrite: true, ct);
                names.Add(blobName);
            }
            await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
            {
                if (names.Contains(blob.Name))
                    continue;
                await container.DeleteBlobAsync(blob.Name, cancellationToken: ct);
                deleted++;
            }
            containers[name] = names.Count;
        }
        return new BackupSummary(tables, containers, deleted);
    }

    /// <summary>One entity as JSON: keys plus every property with its Edm type.</summary>
    public static JsonObject Serialize(TableEntity entity)
    {
        var properties = new JsonObject();
        foreach (var (key, value) in entity)
        {
            if (key is "PartitionKey" or "RowKey" or "Timestamp" or "odata.etag")
                continue;
            properties[key] = value switch
            {
                null => null,
                string s => Typed("String", s),
                bool b => Typed("Boolean", b),
                int i => Typed("Int32", i),
                long l => Typed("Int64", l.ToString(CultureInfo.InvariantCulture)),
                double d => Typed("Double", d),
                DateTimeOffset dto => Typed("DateTime", dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
                DateTime dt => Typed("DateTime", DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)),
                Guid g => Typed("Guid", g.ToString()),
                byte[] bytes => Typed("Binary", Convert.ToBase64String(bytes)),
                BinaryData data => Typed("Binary", Convert.ToBase64String(data.ToArray())),
                _ => throw new NotSupportedException($"Nepodporovaný typ vlastnosti {key}: {value.GetType().Name}"),
            };
        }
        return new JsonObject { ["pk"] = entity.PartitionKey, ["rk"] = entity.RowKey, ["p"] = properties };
    }

    public static TableEntity Deserialize(JsonObject json)
    {
        var entity = new TableEntity(json["pk"]!.GetValue<string>(), json["rk"]!.GetValue<string>());
        foreach (var (key, node) in json["p"]!.AsObject())
        {
            if (node is null)
                continue;
            var type = node["t"]!.GetValue<string>();
            var value = node["v"]!;
            entity[key] = type switch
            {
                "String" => value.GetValue<string>(),
                "Boolean" => value.GetValue<bool>(),
                "Int32" => value.GetValue<int>(),
                "Int64" => long.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture),
                "Double" => value.GetValue<double>(),
                "DateTime" => DateTimeOffset.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                "Guid" => Guid.Parse(value.GetValue<string>()),
                "Binary" => Convert.FromBase64String(value.GetValue<string>()),
                _ => throw new NotSupportedException($"Neznámý typ {type} u vlastnosti {key}"),
            };
        }
        return entity;
    }

    private static JsonObject Typed<T>(string type, T value) => new() { ["t"] = type, ["v"] = JsonValue.Create(value) };
}
