-- Tenants and users required by auth/register/login flows.
CREATE TABLE IF NOT EXISTS public.tenants (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 200),
    key TEXT NOT NULL CHECK (char_length(key) BETWEEN 1 AND 120),
    contact_email TEXT NOT NULL CHECK (char_length(contact_email) BETWEEN 3 AND 320),
    plan_type TEXT NOT NULL CHECK (plan_type IN ('starter', 'pro', 'enterprise', 'single_use')),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tenants_key ON public.tenants(key);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tenants_contact_email ON public.tenants(contact_email);
CREATE INDEX IF NOT EXISTS ix_tenants_is_active ON public.tenants(is_active);

ALTER TABLE public.tenants ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Service role full access on tenants" ON public.tenants
    TO service_role
    USING (true)
    WITH CHECK (true);

CREATE TABLE IF NOT EXISTS public.users (
    id UUID PRIMARY KEY,
    email TEXT NOT NULL CHECK (char_length(email) BETWEEN 3 AND 320),
    full_name TEXT NOT NULL CHECK (char_length(full_name) BETWEEN 2 AND 200),
    phone TEXT NULL,
    birth_date DATE NULL,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL CHECK (role IN ('system_admin', 'admin', 'user', 'player', 'viewer')),
    tenant_id UUID NULL,
    organization_id UUID NULL,
    avatar_url TEXT NULL,
    locale TEXT NULL,
    date_format TEXT NULL,
    is_email_verified BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    verification_code TEXT NULL,
    verification_code_expires_at_utc TIMESTAMPTZ NULL,
    password_reset_token TEXT NULL,
    password_reset_token_expires_at_utc TIMESTAMPTZ NULL,
    refresh_token TEXT NULL,
    refresh_token_expires_at_utc TIMESTAMPTZ NULL,
    CONSTRAINT fk_users_tenant
        FOREIGN KEY (tenant_id)
        REFERENCES public.tenants(id)
        ON DELETE SET NULL,
    CONSTRAINT fk_users_organization
        FOREIGN KEY (organization_id)
        REFERENCES public.organizations(id)
        ON DELETE SET NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email ON public.users(email);
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_phone ON public.users(phone) WHERE phone IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_users_tenant_id ON public.users(tenant_id);
CREATE INDEX IF NOT EXISTS ix_users_organization_id ON public.users(organization_id);
CREATE INDEX IF NOT EXISTS ix_users_refresh_token ON public.users(refresh_token) WHERE refresh_token IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_users_is_active ON public.users(is_active);

ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Service role full access on users" ON public.users
    TO service_role
    USING (true)
    WITH CHECK (true);
