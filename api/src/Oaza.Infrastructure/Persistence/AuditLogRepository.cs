using System.Globalization;
using Azure.Data.Tables;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Helpers;
using Oaza.Domain.Interfaces;

namespace Oaza.Infrastructure.Persistence;

/// <summary>
/// Append-only audit log. PartitionKey = month of the change ("yyyy-MM"), so a
/// date-range read touches only the months it covers; RowKey = inverted
/// timestamp + id, so each partition reads newest first.
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly TableServiceClient _serviceClient;
    private TableClient? _tableClient;

    public AuditLogRepository(TableServiceClient serviceClient)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
    }

    private async Task<TableClient> GetTableClientAsync()
    {
        if (_tableClient is not null) return _tableClient;
        var client = _serviceClient.GetTableClient(TableNames.AuditLog);
        await client.CreateIfNotExistsAsync();
        return _tableClient = client;
    }

    public static string PartitionKeyFor(DateTime timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public async Task AppendAsync(AuditLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var client = await GetTableClientAsync();
        var timestamp = DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Utc);
        var entity = new TableEntity(PartitionKeyFor(timestamp), $"{InvertedTimestamp.FromDateTime(timestamp)}-{entry.Id}")
        {
            { "Id", entry.Id },
            { "Timestamp_", timestamp },
            { "UserId", entry.UserId },
            { "UserName", entry.UserName },
            { "EntityType", entry.EntityType },
            { "EntityId", entry.EntityId },
            { "Action", entry.Action },
            { "OldValue", entry.OldValue },
            { "NewValue", entry.NewValue },
            { "Reason", entry.Reason },
        };

        // AddEntity (not upsert): an audit entry must never overwrite another one.
        await client.AddEntityAsync(entity);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(DateTime from, DateTime to, string? entityType = null, string? entityId = null)
    {
        var client = await GetTableClientAsync();
        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to, DateTimeKind.Utc);
        var results = new List<AuditLogEntry>();

        for (var month = new DateTime(fromUtc.Year, fromUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
             month <= toUtc;
             month = month.AddMonths(1))
        {
            var partition = PartitionKeyFor(month);
            await foreach (var entity in client.QueryAsync<TableEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {partition}")))
            {
                var entry = Map(entity);
                if (entry.Timestamp < fromUtc || entry.Timestamp > toUtc) continue;
                if (entityType is not null && !string.Equals(entry.EntityType, entityType, StringComparison.Ordinal)) continue;
                if (entityId is not null && !string.Equals(entry.EntityId, entityId, StringComparison.Ordinal)) continue;
                results.Add(entry);
            }
        }

        return results.OrderByDescending(e => e.Timestamp).ToList();
    }

    private static AuditLogEntry Map(TableEntity entity) => new()
    {
        Id = entity.GetString("Id") ?? string.Empty,
        Timestamp = entity.GetDateTimeOffset("Timestamp_")?.UtcDateTime ?? DateTime.MinValue,
        UserId = entity.GetString("UserId") ?? string.Empty,
        UserName = entity.GetString("UserName"),
        EntityType = entity.GetString("EntityType") ?? string.Empty,
        EntityId = entity.GetString("EntityId") ?? string.Empty,
        Action = entity.GetString("Action") ?? string.Empty,
        OldValue = entity.GetString("OldValue"),
        NewValue = entity.GetString("NewValue"),
        Reason = entity.GetString("Reason"),
    };
}
