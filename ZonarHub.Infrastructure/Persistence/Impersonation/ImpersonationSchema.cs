namespace ZonarHub.Infrastructure.Persistence.Impersonation;

/// <summary>
/// Authoritative column-name map for the impersonation tables.
/// Both repositories and the migration smoke tests reference this class so
/// that renaming a column only requires a single change.
/// Created by migration: <see cref="MigrationFileName"/>.
/// </summary>
public static class ImpersonationSchema
{
    /// <summary>The name of the SQL migration file that creates these tables.</summary>
    public const string MigrationFileName = "20260515_create_impersonation_tables.sql";

    /// <summary>Column-name constants for the <c>impersonation_sessions</c> table.</summary>
    public static class Sessions
    {
        public const string TableName = "impersonation_sessions";

        public const string Id = "id";
        public const string RealUserId = "real_user_id";
        public const string TargetUserId = "target_user_id";
        public const string StartedAt = "started_at";
        public const string ExpiresAt = "expires_at";
        public const string RevokedAt = "revoked_at";
        public const string TenantId = "tenant_id";
        public const string Reason = "reason";
    }

    /// <summary>Column-name constants for the <c>impersonation_audit</c> table.</summary>
    public static class Audit
    {
        public const string TableName = "impersonation_audit";

        public const string Id = "id";
        public const string SessionId = "session_id";
        public const string RealUserId = "real_user_id";
        public const string EffectiveUserId = "effective_user_id";
        public const string Method = "method";
        public const string Path = "path";
        public const string Status = "status";
        public const string Ip = "ip";
        public const string UserAgent = "user_agent";
        public const string OccurredAt = "occurred_at";
        public const string EventType = "event_type";
    }
}
