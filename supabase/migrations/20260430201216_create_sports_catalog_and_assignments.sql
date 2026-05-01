-- Global sports catalog (fixed for all tenants/organizations)
CREATE TABLE IF NOT EXISTS public.sports (
	id UUID PRIMARY KEY,
	name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 120),
	key TEXT NOT NULL UNIQUE CHECK (char_length(key) BETWEEN 1 AND 80),
	icon TEXT NOT NULL,
	icon_source TEXT NOT NULL CHECK (icon_source IN ('unicode', 'svg')),
	modality_ids UUID[] NOT NULL DEFAULT '{}',
	sort_order INTEGER NOT NULL DEFAULT 0,
	is_active BOOLEAN NOT NULL DEFAULT TRUE,
	created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_sports_key ON public.sports(key);
CREATE INDEX IF NOT EXISTS idx_sports_active_sort ON public.sports(is_active, sort_order);

-- Sports enabled per organization
CREATE TABLE IF NOT EXISTS public.organization_sports (
	organization_id UUID NOT NULL,
	sport_id UUID NOT NULL,
	created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	PRIMARY KEY (organization_id, sport_id),
	CONSTRAINT fk_organization_sports_org
		FOREIGN KEY (organization_id)
		REFERENCES public.organizations(id)
		ON DELETE CASCADE,
	CONSTRAINT fk_organization_sports_sport
		FOREIGN KEY (sport_id)
		REFERENCES public.sports(id)
		ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_organization_sports_org ON public.organization_sports(organization_id);
CREATE INDEX IF NOT EXISTS idx_organization_sports_sport ON public.organization_sports(sport_id);

-- Sports enabled per tenant (optional top-level enablement)
-- Tenant FK intentionally omitted to keep this migration decoupled from tenant-table rollout order.
CREATE TABLE IF NOT EXISTS public.tenant_sports (
	tenant_id UUID NOT NULL,
	sport_id UUID NOT NULL,
	created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	PRIMARY KEY (tenant_id, sport_id),
	CONSTRAINT fk_tenant_sports_sport
		FOREIGN KEY (sport_id)
		REFERENCES public.sports(id)
		ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_tenant_sports_tenant ON public.tenant_sports(tenant_id);
CREATE INDEX IF NOT EXISTS idx_tenant_sports_sport ON public.tenant_sports(sport_id);

ALTER TABLE public.sports ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.organization_sports ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.tenant_sports ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.sports;
CREATE POLICY "Service role full access" ON public.sports
	TO service_role
	USING (true)
	WITH CHECK (true);

DROP POLICY IF EXISTS "Service role full access" ON public.organization_sports;
CREATE POLICY "Service role full access" ON public.organization_sports
	TO service_role
	USING (true)
	WITH CHECK (true);

DROP POLICY IF EXISTS "Service role full access" ON public.tenant_sports;
CREATE POLICY "Service role full access" ON public.tenant_sports
	TO service_role
	USING (true)
	WITH CHECK (true);

-- Fixed sports for the whole application
INSERT INTO public.sports (
	id,
	name,
	key,
	icon,
	icon_source,
	modality_ids,
	sort_order,
	is_active
)
VALUES
	('1c65fdf8-76fc-4a91-a2b5-1d70791bfde1', 'Padel', 'padel', '🎾', 'unicode', '{}', 1, TRUE),
	('f57e77a7-24ba-4fd3-a0d3-0b89df6222d2', 'Tenis', 'tenis', '🎾', 'unicode', '{}', 2, TRUE),
	('293aabec-31db-4f5a-9e22-8eb218f4d11a', 'Futbol', 'futbol', '⚽', 'unicode', '{}', 3, TRUE),
	('725d42b7-2298-4f55-acf4-4ab99f910fd2', 'Rugby', 'rugby', '🏉', 'unicode', '{}', 4, TRUE),
	('0f4f3d79-7e34-4df2-ae2f-93986db2b09c', 'Pickleball', 'pickleball', '🏓', 'unicode', '{}', 5, TRUE)
ON CONFLICT (key)
DO UPDATE SET
	name = EXCLUDED.name,
	icon = EXCLUDED.icon,
	icon_source = EXCLUDED.icon_source,
	sort_order = EXCLUDED.sort_order,
	is_active = EXCLUDED.is_active,
	updated_at_utc = NOW();
