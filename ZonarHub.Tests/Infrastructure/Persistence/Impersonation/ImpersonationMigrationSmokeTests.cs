using ZonarHub.Infrastructure.Persistence.Impersonation;

namespace ZonarHub.Tests.Infrastructure.Persistence.Impersonation;

/// <summary>
/// Smoke tests asserting that the impersonation schema constants match the
/// expected column names, table names, and index names defined in
/// 20260515_create_impersonation_tables.sql.
///
/// These tests do NOT hit a real database; they verify that the schema
/// descriptor class (which is the authoritative column-name map used by
/// repositories) reflects the migration spec.
/// Task 1.1.1 — RED until ImpersonationSchema exists.
/// </summary>
public sealed class ImpersonationMigrationSmokeTests
{
    // ── impersonation_sessions table ─────────────────────────────────────

    [Fact]
    public void SessionsTable_HasCorrectTableName()
    {
        Assert.Equal("impersonation_sessions", ImpersonationSchema.Sessions.TableName);
    }

    [Fact]
    public void SessionsTable_HasIdColumn()
    {
        Assert.Equal("id", ImpersonationSchema.Sessions.Id);
    }

    [Fact]
    public void SessionsTable_HasRealUserIdColumn()
    {
        Assert.Equal("real_user_id", ImpersonationSchema.Sessions.RealUserId);
    }

    [Fact]
    public void SessionsTable_HasTargetUserIdColumn()
    {
        Assert.Equal("target_user_id", ImpersonationSchema.Sessions.TargetUserId);
    }

    [Fact]
    public void SessionsTable_HasStartedAtColumn()
    {
        Assert.Equal("started_at", ImpersonationSchema.Sessions.StartedAt);
    }

    [Fact]
    public void SessionsTable_HasExpiresAtColumn()
    {
        Assert.Equal("expires_at", ImpersonationSchema.Sessions.ExpiresAt);
    }

    [Fact]
    public void SessionsTable_HasRevokedAtColumn()
    {
        Assert.Equal("revoked_at", ImpersonationSchema.Sessions.RevokedAt);
    }

    [Fact]
    public void SessionsTable_HasTenantIdColumn()
    {
        Assert.Equal("tenant_id", ImpersonationSchema.Sessions.TenantId);
    }

    [Fact]
    public void SessionsTable_HasReasonColumn()
    {
        Assert.Equal("reason", ImpersonationSchema.Sessions.Reason);
    }

    // ── impersonation_audit table ─────────────────────────────────────────

    [Fact]
    public void AuditTable_HasCorrectTableName()
    {
        Assert.Equal("impersonation_audit", ImpersonationSchema.Audit.TableName);
    }

    [Fact]
    public void AuditTable_HasIdColumn()
    {
        Assert.Equal("id", ImpersonationSchema.Audit.Id);
    }

    [Fact]
    public void AuditTable_HasSessionIdColumn()
    {
        Assert.Equal("session_id", ImpersonationSchema.Audit.SessionId);
    }

    [Fact]
    public void AuditTable_HasRealUserIdColumn()
    {
        Assert.Equal("real_user_id", ImpersonationSchema.Audit.RealUserId);
    }

    [Fact]
    public void AuditTable_HasEffectiveUserIdColumn()
    {
        Assert.Equal("effective_user_id", ImpersonationSchema.Audit.EffectiveUserId);
    }

    [Fact]
    public void AuditTable_HasMethodColumn()
    {
        Assert.Equal("method", ImpersonationSchema.Audit.Method);
    }

    [Fact]
    public void AuditTable_HasPathColumn()
    {
        Assert.Equal("path", ImpersonationSchema.Audit.Path);
    }

    [Fact]
    public void AuditTable_HasStatusColumn()
    {
        Assert.Equal("status", ImpersonationSchema.Audit.Status);
    }

    [Fact]
    public void AuditTable_HasIpColumn()
    {
        Assert.Equal("ip", ImpersonationSchema.Audit.Ip);
    }

    [Fact]
    public void AuditTable_HasUserAgentColumn()
    {
        Assert.Equal("user_agent", ImpersonationSchema.Audit.UserAgent);
    }

    [Fact]
    public void AuditTable_HasOccurredAtColumn()
    {
        Assert.Equal("occurred_at", ImpersonationSchema.Audit.OccurredAt);
    }

    [Fact]
    public void AuditTable_HasEventTypeColumn()
    {
        Assert.Equal("event_type", ImpersonationSchema.Audit.EventType);
    }

    // ── migration file path ───────────────────────────────────────────────

    [Fact]
    public void MigrationFileName_HasExpectedName()
    {
        // The migration file must follow the naming convention.
        Assert.Equal(
            "20260515_create_impersonation_tables.sql",
            ImpersonationSchema.MigrationFileName);
    }
}
