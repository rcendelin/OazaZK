using System.Text.Json;
using System.Text.Json.Serialization;
using Oaza.Domain.Constants;
using Oaza.Domain.Entities;
using Oaza.Domain.Interfaces;

namespace Oaza.Application.Audit;

public class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly HashSet<string> KnownActions = new(StringComparer.Ordinal)
    {
        AuditActions.Create, AuditActions.Update, AuditActions.Delete, AuditActions.Correction,
    };

    private readonly IAuditLogRepository _repository;
    private readonly TimeProvider _timeProvider;

    public AuditLogger(IAuditLogRepository repository, TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task LogAsync(string entityType, string entityId, string action, object? oldValue, object? newValue, AuditActor actor, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentNullException.ThrowIfNull(actor);
        if (!KnownActions.Contains(action))
            throw new ArgumentException($"Unknown audit action '{action}'.", nameof(action));
        if (action == AuditActions.Correction && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A correction must state its reason.", nameof(reason));

        return _repository.AppendAsync(new AuditLogEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = _timeProvider.GetUtcNow().UtcDateTime,
            UserId = actor.UserId,
            UserName = actor.UserName,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValue = Serialize(oldValue),
            NewValue = Serialize(newValue),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        });
    }

    private static string? Serialize(object? value) =>
        value switch
        {
            null => null,
            string s => s,
            _ => JsonSerializer.Serialize(value, value.GetType(), JsonOptions),
        };
}
