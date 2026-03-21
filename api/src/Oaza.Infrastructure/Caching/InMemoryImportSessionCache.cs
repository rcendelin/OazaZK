using System.Collections.Concurrent;
using Oaza.Application.Interfaces;

namespace Oaza.Infrastructure.Caching;

/// <summary>
/// In-memory cache for import sessions. Sessions expire after 30 minutes.
/// Registered as singleton in DI.
/// </summary>
public class InMemoryImportSessionCache : IImportSessionCache
{
    private readonly ConcurrentDictionary<string, ImportSessionData> _cache = new();
    private static readonly TimeSpan SessionExpiry = TimeSpan.FromMinutes(30);

    public void Store(string sessionId, ImportSessionData data)
    {
        CleanExpiredSessions();
        _cache[sessionId] = data;
    }

    public ImportSessionData? Retrieve(string sessionId)
    {
        if (!_cache.TryGetValue(sessionId, out var data))
        {
            return null;
        }

        if (DateTime.UtcNow - data.CreatedAt > SessionExpiry)
        {
            _cache.TryRemove(sessionId, out _);
            return null;
        }

        return data;
    }

    public void Remove(string sessionId)
    {
        _cache.TryRemove(sessionId, out _);
    }

    private void CleanExpiredSessions()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _cache)
        {
            if (now - kvp.Value.CreatedAt > SessionExpiry)
            {
                _cache.TryRemove(kvp.Key, out _);
            }
        }
    }
}
