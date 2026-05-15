namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Manages impersonation sessions and audit records.
/// Design §2.3: validates <c>revoked_at IS NULL AND expires_at > now() AND id = sessionId</c>.
/// Satisfies REQ-AUD-004, REQ-AUD-005.
/// </summary>
public interface IImpersonationSessionStore
{
    /// <summary>
    /// Returns <c>true</c> when the session exists, is not revoked, and has not expired.
    /// </summary>
    Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new impersonation session row. Returns the new session ID.
    /// Satisfies: REQ-IMP-009, design §4.1.
    /// </summary>
    Task<Guid> CreateAsync(
        ImpersonationSessionRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>revoked_at = now()</c> on the session row identified by <paramref name="sessionId"/>.
    /// No-op (does not throw) if already revoked.
    /// Satisfies: REQ-IMP-020, design §4.1.
    /// </summary>
    Task RevokeAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active session for <paramref name="realUserId"/>, or <c>null</c> if none.
    /// Used to auto-revoke an existing session when a second start is requested
    /// (satisfies REQ-IMP-013 / REQ-IMP-032).
    /// </summary>
    Task<ImpersonationSessionRecord?> GetActiveByRealUserAsync(
        Guid realUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a lifecycle or per-request audit row to <c>impersonation_audit</c>.
    /// MUST be non-throwing: implementation logs and swallows on failure
    /// EXCEPT for the <c>session_started</c> event which the caller treats as blocking.
    /// Satisfies: REQ-AUD-001–003, REQ-AUD-006–011, design §4.3.
    /// </summary>
    Task WriteAuditAsync(
        ImpersonationAuditRecord record,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Value object representing one row in <c>impersonation_sessions</c>.
/// </summary>
public sealed record ImpersonationSessionRecord(
    Guid Id,
    Guid RealUserId,
    Guid TargetUserId,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    Guid TenantId,
    string? Reason);

/// <summary>
/// Value object representing one row in <c>impersonation_audit</c>.
/// </summary>
public sealed record ImpersonationAuditRecord(
    Guid Id,
    Guid SessionId,
    Guid RealUserId,
    Guid EffectiveUserId,
    string Method,
    string Path,
    int? Status,
    string? Ip,
    string? UserAgent,
    DateTimeOffset OccurredAt,
    string EventType);
