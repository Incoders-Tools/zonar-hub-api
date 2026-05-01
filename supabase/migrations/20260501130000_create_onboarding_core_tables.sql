-- Core onboarding persistence tables required by CompleteOnboarding workflow.

CREATE TABLE IF NOT EXISTS public.system_settings (
    id UUID PRIMARY KEY,
    key TEXT NOT NULL CHECK (char_length(key) BETWEEN 1 AND 160),
    value TEXT NOT NULL,
    scope TEXT NOT NULL CHECK (scope IN ('global', 'tenant', 'user')),
    tenant_id UUID NULL,
    user_id UUID NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_system_settings_tenant
        FOREIGN KEY (tenant_id)
        REFERENCES public.tenants(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_system_settings_user
        FOREIGN KEY (user_id)
        REFERENCES public.users(id)
        ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_system_settings_scope_key_owner
    ON public.system_settings (
        lower(key),
        scope,
        coalesce(tenant_id, '00000000-0000-0000-0000-000000000000'::uuid),
        coalesce(user_id, '00000000-0000-0000-0000-000000000000'::uuid)
    );

CREATE INDEX IF NOT EXISTS ix_system_settings_scope ON public.system_settings(scope);
CREATE INDEX IF NOT EXISTS ix_system_settings_tenant ON public.system_settings(tenant_id);
CREATE INDEX IF NOT EXISTS ix_system_settings_user ON public.system_settings(user_id);

CREATE TABLE IF NOT EXISTS public.complexes (
    id UUID PRIMARY KEY,
    organization_id UUID NOT NULL,
    name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 200),
    address TEXT NOT NULL CHECK (char_length(address) BETWEEN 1 AND 400),
    location TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_complexes_organization
        FOREIGN KEY (organization_id)
        REFERENCES public.organizations(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_complexes_organization_id ON public.complexes(organization_id);
CREATE INDEX IF NOT EXISTS ix_complexes_is_active ON public.complexes(is_active);

CREATE TABLE IF NOT EXISTS public.courts (
    id UUID PRIMARY KEY,
    complex_id UUID NOT NULL,
    name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 120),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_courts_complex
        FOREIGN KEY (complex_id)
        REFERENCES public.complexes(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_courts_complex_id ON public.courts(complex_id);
CREATE INDEX IF NOT EXISTS ix_courts_complex_name ON public.courts(complex_id, name);

CREATE TABLE IF NOT EXISTS public.tournaments (
    id UUID PRIMARY KEY,
    organization_id UUID NOT NULL,
    complex_id UUID NULL,
    sport_id UUID NOT NULL,
    name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 200),
    start_date DATE NOT NULL,
    end_date DATE NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('upcoming', 'active', 'finished', 'cancelled')),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_tournaments_organization
        FOREIGN KEY (organization_id)
        REFERENCES public.organizations(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_tournaments_complex
        FOREIGN KEY (complex_id)
        REFERENCES public.complexes(id)
        ON DELETE SET NULL,
    CONSTRAINT fk_tournaments_sport
        FOREIGN KEY (sport_id)
        REFERENCES public.sports(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_tournaments_date_range CHECK (end_date >= start_date)
);

CREATE INDEX IF NOT EXISTS ix_tournaments_org ON public.tournaments(organization_id);
CREATE INDEX IF NOT EXISTS ix_tournaments_complex ON public.tournaments(complex_id);
CREATE INDEX IF NOT EXISTS ix_tournaments_status ON public.tournaments(status);

ALTER TABLE public.system_settings ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.complexes ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.courts ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.tournaments ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.system_settings;
CREATE POLICY "Service role full access" ON public.system_settings
    TO service_role
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS "Service role full access" ON public.complexes;
CREATE POLICY "Service role full access" ON public.complexes
    TO service_role
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS "Service role full access" ON public.courts;
CREATE POLICY "Service role full access" ON public.courts
    TO service_role
    USING (true)
    WITH CHECK (true);

DROP POLICY IF EXISTS "Service role full access" ON public.tournaments;
CREATE POLICY "Service role full access" ON public.tournaments
    TO service_role
    USING (true)
    WITH CHECK (true);
