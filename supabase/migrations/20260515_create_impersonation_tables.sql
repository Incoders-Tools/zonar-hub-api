-- Migration: create impersonation_sessions and impersonation_audit tables.
-- Satisfies: REQ-AUD-001, REQ-AUD-002, REQ-AUD-014, REQ-AUD-015, REQ-AUD-016, design §4.3.
-- RLS: both tables restricted to service_role (system_admin permission enforced at API layer).

-- ── impersonation_sessions ────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS public.impersonation_sessions (
    id            UUID        PRIMARY KEY,
    real_user_id  UUID        NOT NULL,
    target_user_id UUID       NOT NULL,
    started_at    TIMESTAMPTZ NOT NULL,
    expires_at    TIMESTAMPTZ NOT NULL,
    revoked_at    TIMESTAMPTZ NULL,
    tenant_id     UUID        NOT NULL,
    reason        TEXT        NULL CHECK (char_length(reason) <= 500)
);

-- Indexes referenced by queries in IImpersonationSessionStore
CREATE INDEX IF NOT EXISTS ix_impersonation_sessions_real_user_id
    ON public.impersonation_sessions (real_user_id);

CREATE INDEX IF NOT EXISTS ix_impersonation_sessions_target_user_id
    ON public.impersonation_sessions (target_user_id);

CREATE INDEX IF NOT EXISTS ix_impersonation_sessions_expires_at
    ON public.impersonation_sessions (expires_at DESC);

-- RLS: restrict to service_role only; application-level checks enforce system_admin.
ALTER TABLE public.impersonation_sessions ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Service role full access on impersonation_sessions"
    ON public.impersonation_sessions
    TO service_role
    USING (true)
    WITH CHECK (true);

-- ── impersonation_audit ───────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS public.impersonation_audit (
    id                UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id        UUID        NOT NULL REFERENCES public.impersonation_sessions (id),
    real_user_id      UUID        NOT NULL,
    effective_user_id UUID        NOT NULL,
    method            TEXT        NOT NULL,
    path              TEXT        NOT NULL,
    status            INT         NULL,
    ip                TEXT        NULL,
    user_agent        TEXT        NULL,
    occurred_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    event_type        TEXT        NULL
        CHECK (event_type IS NULL OR event_type IN (
            'session_started',
            'session_stopped',
            'request'
        ))
);

-- Indexes for the queries described in design §4.3 and REQ-AUD-015.
CREATE INDEX IF NOT EXISTS ix_impersonation_audit_session_id
    ON public.impersonation_audit (session_id);

CREATE INDEX IF NOT EXISTS ix_impersonation_audit_real_user_id
    ON public.impersonation_audit (real_user_id);

CREATE INDEX IF NOT EXISTS ix_impersonation_audit_effective_user_id
    ON public.impersonation_audit (effective_user_id);

CREATE INDEX IF NOT EXISTS ix_impersonation_audit_occurred_at_desc
    ON public.impersonation_audit (occurred_at DESC);

-- RLS: restrict to service_role only (REQ-AUD-016 immutability: no UPDATE/DELETE policy).
ALTER TABLE public.impersonation_audit ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Service role insert on impersonation_audit"
    ON public.impersonation_audit
    TO service_role
    USING (true)
    WITH CHECK (true);
