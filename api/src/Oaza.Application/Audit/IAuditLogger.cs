namespace Oaza.Application.Audit;

/// <summary>
/// Records changes to the audit log (rule 9). Every use case that creates,
/// changes, closes or corrects accounting data (T02, T03, T05, T08, T09, T10)
/// calls this after a successful write.
/// </summary>
public interface IAuditLogger
{
    /// <param name="entityType">Entity kind, e.g. "CostComponent".</param>
    /// <param name="entityId">Id of the changed entity.</param>
    /// <param name="action">One of <see cref="Domain.Constants.AuditActions"/>.</param>
    /// <param name="oldValue">State before (null for create) — serialized to JSON.</param>
    /// <param name="newValue">State after (null for delete) — serialized to JSON.</param>
    /// <param name="actor">Who made the change.</param>
    /// <param name="reason">Why; required for <see cref="Domain.Constants.AuditActions.Correction"/>.</param>
    Task LogAsync(string entityType, string entityId, string action, object? oldValue, object? newValue, AuditActor actor, string? reason = null);
}

public record AuditActor(string UserId, string? UserName);
