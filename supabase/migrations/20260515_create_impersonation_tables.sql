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

-- RLS does not restrict TRUNCATE and service_role bypasses RLS, so drop default
-- grants and allow only the API store's read, create and revoke operations.
REVOKE ALL ON TABLE public.impersonation_sessions FROM PUBLIC, anon, authenticated, service_role;
GRANT SELECT, INSERT, UPDATE ON TABLE public.impersonation_sessions TO service_role;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policy
         WHERE polrelid = 'public.impersonation_sessions'::regclass
           AND polname = 'Service role full access on impersonation_sessions'
    ) THEN
        CREATE POLICY "Service role full access on impersonation_sessions"
            ON public.impersonation_sessions
            TO service_role
            USING (true)
            WITH CHECK (true);
    END IF;
END $$;

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

-- service_role bypasses RLS, so append-only must also hold at the privilege level:
-- drop default grants and allow application roles to insert and read only.
REVOKE ALL ON TABLE public.impersonation_audit FROM PUBLIC, anon, authenticated, service_role;
GRANT SELECT, INSERT ON TABLE public.impersonation_audit TO service_role;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policy
         WHERE polrelid = 'public.impersonation_audit'::regclass
           AND polname = 'Service role insert on impersonation_audit'
    ) THEN
        CREATE POLICY "Service role insert on impersonation_audit"
            ON public.impersonation_audit
            FOR INSERT
            TO service_role
            WITH CHECK (true);
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_policy
         WHERE polrelid = 'public.impersonation_audit'::regclass
           AND polname = 'Service role select on impersonation_audit'
    ) THEN
        CREATE POLICY "Service role select on impersonation_audit"
            ON public.impersonation_audit
            FOR SELECT
            TO service_role
            USING (true);
    END IF;
END $$;

-- ── Replay guard ──────────────────────────────────────────────────────────────
-- IF NOT EXISTS skips objects that already exist, so fail closed when a reused
-- name differs from this migration, including extra constraints, indexes or policies,
-- policy commands, and table or column privileges beyond service_role SELECT/INSERT/UPDATE
-- on sessions and SELECT/INSERT on audit. The table owner keeps its implicit rights.
DO $$
DECLARE
    previous_path text := current_setting('search_path');
    drift text;
BEGIN
    -- Render catalog definitions fully qualified for this comparison only.
    PERFORM set_config('search_path', 'pg_catalog', true);
    WITH owned(rel) AS (
        VALUES ('public.impersonation_sessions'::regclass), ('public.impersonation_audit'::regclass)
    ), expected(item) AS (VALUES
        ('public.impersonation_sessions columns: id uuid not null, real_user_id uuid not null, '
         || 'target_user_id uuid not null, started_at timestamp with time zone not null, '
         || 'expires_at timestamp with time zone not null, revoked_at timestamp with time zone, '
         || 'tenant_id uuid not null, reason text'),
        ('public.impersonation_sessions PRIMARY KEY (id)'),
        ('public.impersonation_sessions CHECK ((char_length(reason) <= 500))'),
        ('CREATE INDEX ix_impersonation_sessions_real_user_id ON public.impersonation_sessions USING btree (real_user_id)'),
        ('CREATE INDEX ix_impersonation_sessions_target_user_id ON public.impersonation_sessions USING btree (target_user_id)'),
        ('CREATE INDEX ix_impersonation_sessions_expires_at ON public.impersonation_sessions USING btree (expires_at DESC)'),
        ('public.impersonation_sessions rls true'),
        ('public.impersonation_sessions policy "Service role full access on impersonation_sessions" '
         || 'permissive * {service_role} using true check true'),
        ('public.impersonation_sessions acl service_role INSERT, service_role SELECT, service_role UPDATE'),
        ('public.impersonation_audit columns: id uuid not null default gen_random_uuid(), '
         || 'session_id uuid not null, real_user_id uuid not null, effective_user_id uuid not null, '
         || 'method text not null, path text not null, status integer, ip text, user_agent text, '
         || 'occurred_at timestamp with time zone not null default now(), event_type text'),
        ('public.impersonation_audit PRIMARY KEY (id)'),
        ('public.impersonation_audit FOREIGN KEY (session_id) REFERENCES public.impersonation_sessions(id)'),
        ('public.impersonation_audit CHECK (((event_type IS NULL) OR (event_type = ANY '
         || '(ARRAY[''session_started''::text, ''session_stopped''::text, ''request''::text]))))'),
        ('CREATE INDEX ix_impersonation_audit_session_id ON public.impersonation_audit USING btree (session_id)'),
        ('CREATE INDEX ix_impersonation_audit_real_user_id ON public.impersonation_audit USING btree (real_user_id)'),
        ('CREATE INDEX ix_impersonation_audit_effective_user_id ON public.impersonation_audit USING btree (effective_user_id)'),
        ('CREATE INDEX ix_impersonation_audit_occurred_at_desc ON public.impersonation_audit USING btree (occurred_at DESC)'),
        ('public.impersonation_audit rls true'),
        ('public.impersonation_audit policy "Service role insert on impersonation_audit" '
         || 'permissive a {service_role} using none check true'),
        ('public.impersonation_audit policy "Service role select on impersonation_audit" '
         || 'permissive r {service_role} using true check none'),
        ('public.impersonation_audit acl service_role INSERT, service_role SELECT')
    ), actual(item) AS (
        SELECT format('%s columns: %s', a.attrelid::regclass, string_agg(
                   format('%s %s', a.attname, format_type(a.atttypid, a.atttypmod))
                   || CASE WHEN a.attnotnull THEN ' not null' ELSE '' END
                   || coalesce(' default ' || pg_get_expr(d.adbin, d.adrelid), ''),
                   ', ' ORDER BY a.attnum))
          FROM pg_attribute a
          JOIN owned ON a.attrelid = owned.rel
          LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
         WHERE a.attnum > 0 AND NOT a.attisdropped
         GROUP BY a.attrelid
        UNION ALL
        SELECT format('%s %s', c.conrelid::regclass, pg_get_constraintdef(c.oid))
          FROM pg_constraint c JOIN owned ON c.conrelid = owned.rel
        UNION ALL
        SELECT pg_get_indexdef(i.indexrelid)
          FROM pg_index i JOIN owned ON i.indrelid = owned.rel
         WHERE NOT i.indisprimary
        UNION ALL
        SELECT format('%s rls %s', t.oid::regclass, t.relrowsecurity::text)
          FROM pg_class t JOIN owned ON t.oid = owned.rel
        UNION ALL
        SELECT format('%s policy %I %s %s %s using %s check %s', p.polrelid::regclass, p.polname,
                   CASE WHEN p.polpermissive THEN 'permissive' ELSE 'restrictive' END, p.polcmd,
                   p.polroles::regrole[], coalesce(pg_get_expr(p.polqual, p.polrelid), 'none'),
                   coalesce(pg_get_expr(p.polwithcheck, p.polrelid), 'none'))
          FROM pg_policy p JOIN owned ON p.polrelid = owned.rel
        UNION ALL
        -- Non-owner table privileges; the owner keeps its implicit maintenance rights.
        SELECT format('%s acl %s', t.oid::regclass, coalesce((
                   SELECT string_agg(g.item, ', ' ORDER BY g.item)
                     FROM (SELECT format('%s %s%s',
                                  CASE WHEN e.grantee = 0 THEN 'PUBLIC' ELSE e.grantee::regrole::text END,
                                  e.privilege_type, CASE WHEN e.is_grantable THEN ' grantable' ELSE '' END)
                             FROM aclexplode(coalesce(t.relacl, acldefault('r', t.relowner))) e
                            WHERE e.grantee <> t.relowner) g(item)), 'none'))
          FROM pg_class t JOIN owned ON t.oid = owned.rel
        UNION ALL
        SELECT format('%s column %s acl %s', a.attrelid::regclass, a.attname, a.attacl)
          FROM pg_attribute a JOIN owned ON a.attrelid = owned.rel
         WHERE cardinality(a.attacl) > 0
    )
    SELECT string_agg(item, '; ') INTO drift
      FROM ((SELECT item FROM expected EXCEPT ALL SELECT item FROM actual)
            UNION ALL
            (SELECT item FROM actual EXCEPT ALL SELECT item FROM expected)) mismatch;
    PERFORM set_config('search_path', previous_path, true);
    IF drift IS NOT NULL THEN
        RAISE EXCEPTION 'impersonation schema drift: %', drift;
    END IF;
END $$;
