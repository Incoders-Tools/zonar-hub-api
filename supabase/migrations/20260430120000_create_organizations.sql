-- Organizations table for ZonarHub
CREATE TABLE IF NOT EXISTS public.organizations (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    display_name TEXT NOT NULL CHECK (char_length(display_name) BETWEEN 1 AND 200),
    legal_name TEXT CHECK (legal_name IS NULL OR char_length(legal_name) <= 300),
    description TEXT,
    type TEXT NOT NULL CHECK (type IN ('estandar', 'circuito', 'academia', 'operadora', 'marca')),
    logo_url TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by_user_id UUID NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_organizations_tenant_id ON public.organizations(tenant_id);
CREATE INDEX IF NOT EXISTS idx_organizations_type ON public.organizations(type);
CREATE INDEX IF NOT EXISTS idx_organizations_is_active ON public.organizations(is_active);
CREATE INDEX IF NOT EXISTS idx_organizations_tenant_active ON public.organizations(tenant_id, is_active);

ALTER TABLE public.organizations ENABLE ROW LEVEL SECURITY;

-- Service role bypasses RLS; anon/authenticated policies can be added later per business rules
CREATE POLICY "Service role full access" ON public.organizations
    TO service_role
    USING (true)
    WITH CHECK (true);
