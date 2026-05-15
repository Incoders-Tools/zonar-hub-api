using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ZonarHub.Application.Abstractions;
using ZonarHub.Infrastructure.Persistence.Impersonation;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

/// <summary>
/// Supabase-backed implementation of <see cref="IImpersonationSessionStore"/>.
/// Talks to <c>impersonation_sessions</c> and <c>impersonation_audit</c> via PostgREST.
///
/// Satisfies: design §2.3, §4.3; task 2.1 (Supabase impl).
/// </summary>
internal sealed class SupabaseImpersonationSessionStore : IImpersonationSessionStore
{
    private readonly HttpClient _http;
    private readonly ILogger<SupabaseImpersonationSessionStore> _logger;

    private static string SessionsPath => $"/rest/v1/{ImpersonationSchema.Sessions.TableName}";
    private static string AuditPath => $"/rest/v1/{ImpersonationSchema.Audit.TableName}";

    public SupabaseImpersonationSessionStore(
        IHttpClientFactory factory,
        ILogger<SupabaseImpersonationSessionStore> logger)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var url = $"{SessionsPath}" +
                  $"?{ImpersonationSchema.Sessions.Id}=eq.{sessionId}" +
                  $"&{ImpersonationSchema.Sessions.RevokedAt}=is.null" +
                  $"&{ImpersonationSchema.Sessions.ExpiresAt}=gt.{DateTimeOffset.UtcNow:o}" +
                  $"&select={ImpersonationSchema.Sessions.Id}&limit=1";

        var rows = await _http.GetFromJsonAsync<List<IdRow>>(url, cancellationToken);
        return rows is { Count: > 0 };
    }

    /// <inheritdoc/>
    public async Task<Guid> CreateAsync(ImpersonationSessionRecord record, CancellationToken cancellationToken = default)
    {
        var row = new SessionInsertRow(
            record.Id,
            record.RealUserId,
            record.TargetUserId,
            record.StartedAt,
            record.ExpiresAt,
            record.TenantId,
            record.Reason);

        using var req = new HttpRequestMessage(HttpMethod.Post, SessionsPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);

        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        return record.Id;
    }

    /// <inheritdoc/>
    public async Task RevokeAsync(Guid sessionId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        var url = $"{SessionsPath}?{ImpersonationSchema.Sessions.Id}=eq.{sessionId}";
        var patch = new SessionRevokeRow(revokedAt);

        using var req = new HttpRequestMessage(HttpMethod.Patch, url);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(patch);

        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public async Task<ImpersonationSessionRecord?> GetActiveByRealUserAsync(
        Guid realUserId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{SessionsPath}" +
                  $"?{ImpersonationSchema.Sessions.RealUserId}=eq.{realUserId}" +
                  $"&{ImpersonationSchema.Sessions.RevokedAt}=is.null" +
                  $"&{ImpersonationSchema.Sessions.ExpiresAt}=gt.{DateTimeOffset.UtcNow:o}" +
                  $"&select=*&limit=1";

        var rows = await _http.GetFromJsonAsync<List<SessionRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc/>
    public async Task WriteAuditAsync(ImpersonationAuditRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            var row = new AuditInsertRow(
                record.Id,
                record.SessionId,
                record.RealUserId,
                record.EffectiveUserId,
                record.Method,
                record.Path,
                record.Status,
                record.Ip,
                record.UserAgent,
                record.OccurredAt,
                record.EventType);

            using var req = new HttpRequestMessage(HttpMethod.Post, AuditPath);
            req.Headers.Add("Prefer", "return=minimal");
            req.Content = JsonContent.Create(row);

            using var resp = await _http.SendAsync(req, cancellationToken);
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write impersonation audit row (event={EventType}, session={SessionId}). " +
                "The request itself is NOT affected.",
                record.EventType, record.SessionId);
            // Non-throwing by contract (REQ-AUD-009).
        }
    }

    private static ImpersonationSessionRecord ToRecord(SessionRow row) =>
        new(
            Id: row.Id,
            RealUserId: row.RealUserId,
            TargetUserId: row.TargetUserId,
            StartedAt: row.StartedAt,
            ExpiresAt: row.ExpiresAt,
            RevokedAt: row.RevokedAt,
            TenantId: row.TenantId,
            Reason: row.Reason);

    // ─── Row shapes ──────────────────────────────────────────────────────────

    private sealed record IdRow(
        [property: JsonPropertyName("id")] Guid Id);

    private sealed record SessionRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("real_user_id")] Guid RealUserId,
        [property: JsonPropertyName("target_user_id")] Guid TargetUserId,
        [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
        [property: JsonPropertyName("revoked_at")] DateTimeOffset? RevokedAt,
        [property: JsonPropertyName("tenant_id")] Guid TenantId,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record SessionInsertRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("real_user_id")] Guid RealUserId,
        [property: JsonPropertyName("target_user_id")] Guid TargetUserId,
        [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
        [property: JsonPropertyName("tenant_id")] Guid TenantId,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record SessionRevokeRow(
        [property: JsonPropertyName("revoked_at")] DateTimeOffset RevokedAt);

    private sealed record AuditInsertRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("session_id")] Guid SessionId,
        [property: JsonPropertyName("real_user_id")] Guid RealUserId,
        [property: JsonPropertyName("effective_user_id")] Guid EffectiveUserId,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("status")] int? Status,
        [property: JsonPropertyName("ip")] string? Ip,
        [property: JsonPropertyName("user_agent")] string? UserAgent,
        [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
        [property: JsonPropertyName("event_type")] string EventType);
}
