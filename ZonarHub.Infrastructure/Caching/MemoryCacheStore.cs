using System.Collections.Concurrent;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Caching;

/// <summary>
/// Process-local cache implementation.
/// </summary>
public sealed class MemoryCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private readonly TimeProvider _timeProvider;

    public MemoryCacheStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAtUtc is { } expiresAt && expiresAt <= _timeProvider.GetUtcNow())
            {
                _entries.TryRemove(key, out _);
            }
            else if (entry.Value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var expiresAt = ttl.HasValue ? _timeProvider.GetUtcNow().Add(ttl.Value) : (DateTimeOffset?)null;
        _entries[key] = new CacheEntry(value, expiresAt);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(object? Value, DateTimeOffset? ExpiresAtUtc);
}
