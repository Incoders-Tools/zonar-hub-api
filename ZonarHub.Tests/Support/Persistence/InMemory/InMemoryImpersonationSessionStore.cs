using System.Collections.Concurrent;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IImpersonationSessionStore"/>.
/// Used in unit/integration test harnesses. Not thread-safe for concurrent mutations
/// but safe for sequential test code.
/// Satisfies: design §2.3 (test seam for Phase 2 handler tests).
/// </summary>
public sealed class InMemoryImpersonationSessionStore : IImpersonationSessionStore
{
    private readonly ConcurrentDictionary<Guid, ImpersonationSessionRecord> _sessions = new();
    private readonly Func<DateTimeOffset> _utcNow;

    public InMemoryImpersonationSessionStore(Func<DateTimeOffset>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>All audit rows written during the test, inspectable after the act.</summary>
    public List<ImpersonationAuditRecord> AuditRows { get; } = [];

    public Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return Task.FromResult(false);

        var now = _utcNow();
        var active = session.RevokedAt is null && session.ExpiresAt > now;
        return Task.FromResult(active);
    }

    public Task<Guid> CreateAsync(ImpersonationSessionRecord record, CancellationToken cancellationToken = default)
    {
        _sessions[record.Id] = record;
        return Task.FromResult(record.Id);
    }

    public Task RevokeAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            _sessions[sessionId] = existing with { RevokedAt = revokedAt };
        }

        return Task.CompletedTask;
    }

    public Task<ImpersonationSessionRecord?> GetActiveByRealUserAsync(
        Guid realUserId,
        CancellationToken cancellationToken = default)
    {
        var now = _utcNow();
        var active = _sessions.Values
            .FirstOrDefault(s => s.RealUserId == realUserId
                                 && s.RevokedAt is null
                                 && s.ExpiresAt > now);

        return Task.FromResult(active);
    }

    public Task WriteAuditAsync(ImpersonationAuditRecord record, CancellationToken cancellationToken = default)
    {
        AuditRows.Add(record);
        return Task.CompletedTask;
    }

    /// <summary>Returns the session row by id, or null.</summary>
    public ImpersonationSessionRecord? FindSession(Guid sessionId)
    {
        _sessions.TryGetValue(sessionId, out var s);
        return s;
    }
}
