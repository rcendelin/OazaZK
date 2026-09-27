namespace Oaza.Domain.Entities;

/// <summary>
/// One recorded change (rule 9 of the brief): who changed which entity, when,
/// from what to what, and why. Old/new values are JSON snapshots so any entity
/// shape can be stored. Entries are append-only — never updated or deleted.
/// </summary>
public class AuditLogEntry
{
    /// <summary>Unique id; also the RowKey suffix.</summary>
    public string Id { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name at the time of the change (users can be renamed or deleted later).</summary>
    public string? UserName { get; set; }

    /// <summary>Entity kind, e.g. "CostComponent", "Participation", "CashBookEntry".</summary>
    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>See <see cref="Constants.AuditActions"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>JSON snapshot before the change (null for Create).</summary>
    public string? OldValue { get; set; }

    /// <summary>JSON snapshot after the change (null for Delete).</summary>
    public string? NewValue { get; set; }

    /// <summary>Why the change was made — mandatory for corrections of locked data.</summary>
    public string? Reason { get; set; }
}
