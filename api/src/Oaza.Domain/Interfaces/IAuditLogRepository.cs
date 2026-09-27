using Oaza.Domain.Entities;

namespace Oaza.Domain.Interfaces;

public interface IAuditLogRepository
{
    Task AppendAsync(AuditLogEntry entry);

    /// <summary>
    /// Entries with <c>from &lt;= Timestamp &lt;= to</c>, newest first, optionally
    /// filtered by entity type and id.
    /// </summary>
    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(DateTime from, DateTime to, string? entityType = null, string? entityId = null);
}
