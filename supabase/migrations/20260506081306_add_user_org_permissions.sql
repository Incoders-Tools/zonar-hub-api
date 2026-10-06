-- Dynamic permission catalog and user-by-organization tool assignments.

CREATE TABLE IF NOT EXISTS public.system_modules (
	id TEXT PRIMARY KEY,
	key TEXT NOT NULL UNIQUE CHECK (char_length(key) BETWEEN 2 AND 80),
	label_key TEXT NOT NULL CHECK (char_length(label_key) BETWEEN 3 AND 180),
	sort_order INTEGER NOT NULL DEFAULT 0,
	is_active BOOLEAN NOT NULL DEFAULT TRUE,
	created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	CONSTRAINT ck_system_modules_sort_order CHECK (sort_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_system_modules_sort_order
	ON public.system_modules(sort_order);

ALTER TABLE public.system_modules ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.system_modules;
CREATE POLICY "Service role full access" ON public.system_modules
	TO service_role
	USING (true)
	WITH CHECK (true);

CREATE TABLE IF NOT EXISTS public.system_tools (
	id TEXT PRIMARY KEY,
	key TEXT NOT NULL UNIQUE CHECK (char_length(key) BETWEEN 2 AND 120),
	module_id TEXT NOT NULL,
	label_key TEXT NOT NULL CHECK (char_length(label_key) BETWEEN 3 AND 180),
	route TEXT NULL,
	sort_order INTEGER NOT NULL DEFAULT 0,
	is_system_admin_only BOOLEAN NOT NULL DEFAULT FALSE,
	is_active BOOLEAN NOT NULL DEFAULT TRUE,
	created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	CONSTRAINT fk_system_tools_module
		FOREIGN KEY (module_id)
		REFERENCES public.system_modules(id)
		ON UPDATE CASCADE
		ON DELETE RESTRICT,
	CONSTRAINT ck_system_tools_sort_order CHECK (sort_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_system_tools_module_id
	ON public.system_tools(module_id);

CREATE INDEX IF NOT EXISTS ix_system_tools_sort_order
	ON public.system_tools(module_id, sort_order);

CREATE INDEX IF NOT EXISTS ix_system_tools_system_admin_only
	ON public.system_tools(is_system_admin_only);

ALTER TABLE public.system_tools ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.system_tools;
CREATE POLICY "Service role full access" ON public.system_tools
	TO service_role
	USING (true)
	WITH CHECK (true);

CREATE TABLE IF NOT EXISTS public.user_organization_permissions (
	user_id UUID NOT NULL,
	organization_id UUID NOT NULL,
	tool_key TEXT NOT NULL,
	created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	PRIMARY KEY (user_id, organization_id, tool_key),
	CONSTRAINT fk_user_org_permissions_user
		FOREIGN KEY (user_id)
		REFERENCES public.users(id)
		ON DELETE CASCADE,
	CONSTRAINT fk_user_org_permissions_organization
		FOREIGN KEY (organization_id)
		REFERENCES public.organizations(id)
		ON DELETE CASCADE,
	CONSTRAINT fk_user_org_permissions_tool
		FOREIGN KEY (tool_key)
		REFERENCES public.system_tools(key)
		ON UPDATE CASCADE
		ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_user_org_permissions_user_id
	ON public.user_organization_permissions(user_id);

CREATE INDEX IF NOT EXISTS ix_user_org_permissions_organization_id
	ON public.user_organization_permissions(organization_id);

CREATE INDEX IF NOT EXISTS ix_user_org_permissions_tool_key
	ON public.user_organization_permissions(tool_key);

ALTER TABLE public.user_organization_permissions ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.user_organization_permissions;
CREATE POLICY "Service role full access" ON public.user_organization_permissions
	TO service_role
	USING (true)
	WITH CHECK (true);

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.system_modules TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.system_tools TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.user_organization_permissions TO service_role;

INSERT INTO public.system_modules (id, key, label_key, sort_order, is_active)
VALUES
	('module_dashboard', 'dashboard', 'admin.permissions.module.dashboard', 1, TRUE),
	('module_circuit', 'circuit', 'admin.permissions.module.circuit', 2, TRUE),
	('module_catalog', 'catalog', 'admin.permissions.module.catalog', 3, TRUE),
	('module_system', 'system', 'admin.permissions.module.system', 4, TRUE)
ON CONFLICT (key) DO UPDATE
SET
	label_key = EXCLUDED.label_key,
	sort_order = EXCLUDED.sort_order,
	is_active = EXCLUDED.is_active,
	updated_at_utc = NOW()
-- Replays leave identical seed rows, and their updated_at_utc, untouched.
WHERE (system_modules.label_key, system_modules.sort_order, system_modules.is_active)
	IS DISTINCT FROM (EXCLUDED.label_key, EXCLUDED.sort_order, EXCLUDED.is_active);

INSERT INTO public.system_tools (
	id,
	key,
	module_id,
	label_key,
	route,
	sort_order,
	is_system_admin_only,
	is_active)
VALUES
	('tool_dashboard', 'dashboard', 'module_dashboard', 'admin.dashboard', '/admin', 1, FALSE, TRUE),

	('tool_tournaments', 'tournaments', 'module_circuit', 'admin.tournaments', '/admin/tournaments', 1, FALSE, TRUE),
	('tool_tournament_eligibility_profiles', 'tournament-eligibility-profiles', 'module_circuit', 'admin.tournamentEligibilityProfiles', '/admin/catalogs/tournament-eligibility-profiles', 2, FALSE, TRUE),
	('tool_tournament_rules', 'tournament-rules', 'module_circuit', 'admin.tournamentRuleSets', '/admin/catalogs/tournament-rules', 3, FALSE, TRUE),
	('tool_registrations', 'registrations', 'module_circuit', 'admin.registrations', '/admin/registrations', 4, FALSE, TRUE),
	('tool_players', 'players', 'module_circuit', 'admin.players', '/admin/players', 5, FALSE, TRUE),
	('tool_teams', 'teams', 'module_circuit', 'admin.teams', '/admin/teams', 6, FALSE, TRUE),
	('tool_draw_planner', 'draw-planner', 'module_circuit', 'admin.drawPlanner', '/admin/draw-planner', 7, FALSE, TRUE),

	('tool_complexes', 'complexes', 'module_catalog', 'admin.complexes', '/admin/catalogs/complexes', 1, FALSE, TRUE),
	('tool_categories', 'categories', 'module_catalog', 'admin.categories', '/admin/catalogs/categories', 2, FALSE, TRUE),
	('tool_genders', 'genders', 'module_catalog', 'admin.genders', '/admin/catalogs/genders', 3, FALSE, TRUE),
	('tool_sports', 'sports', 'module_catalog', 'admin.sports', '/admin/catalogs/sports', 4, FALSE, TRUE),
	('tool_tournament_statuses', 'tournament-statuses', 'module_catalog', 'admin.tournamentStatuses', '/admin/catalogs/tournament-statuses', 5, FALSE, TRUE),
	('tool_tournament_modalities', 'tournament-modalities', 'module_catalog', 'admin.tournamentModalities', '/admin/catalogs/tournament-modalities', 6, FALSE, TRUE),
	('tool_flyer_backgrounds', 'flyer-backgrounds', 'module_catalog', 'admin.flyerBackgrounds', '/admin/flyer-backgrounds', 7, FALSE, TRUE),

	('tool_users', 'users', 'module_system', 'admin.users', '/admin/system/users', 1, FALSE, TRUE),
	('tool_roles', 'roles', 'module_system', 'admin.roles', '/admin/system/roles', 2, TRUE, TRUE),
	('tool_tenants', 'tenants', 'module_system', 'admin.tenants', '/admin/system/tenants', 3, FALSE, TRUE),
	('tool_organizations', 'organizations', 'module_system', 'admin.organizations', '/admin/system/organizations', 4, FALSE, TRUE),
	('tool_plans', 'plans', 'module_system', 'admin.plans', '/admin/system/plans', 5, TRUE, TRUE),
	('tool_actions', 'actions', 'module_system', 'admin.nav.actions', '/admin/system/actions', 6, TRUE, TRUE),
	('tool_audit', 'audit', 'module_system', 'admin.audit', '/admin/system/audit', 7, TRUE, TRUE),
	('tool_app_logs', 'app-logs', 'module_system', 'admin.appLogs', '/admin/system/logs', 8, TRUE, TRUE),
	('tool_security', 'security', 'module_system', 'admin.security', '/admin/system/security', 9, TRUE, TRUE),
	('tool_settings', 'settings', 'module_system', 'admin.settings', '/admin/system/settings', 10, FALSE, TRUE),
	('tool_email_templates', 'email-templates', 'module_system', 'admin.emailTemplates', '/admin/system/email-templates', 11, TRUE, TRUE),
	('tool_billing', 'billing', 'module_system', 'admin.billing', '/admin/billing', 12, TRUE, TRUE)
ON CONFLICT (key) DO UPDATE
SET
	module_id = EXCLUDED.module_id,
	label_key = EXCLUDED.label_key,
	route = EXCLUDED.route,
	sort_order = EXCLUDED.sort_order,
	-- 20260507100000 opens plans/billing to admins; a replay must not restrict them again.
	is_system_admin_only = CASE WHEN system_tools.key IN ('plans', 'billing')
		THEN system_tools.is_system_admin_only ELSE EXCLUDED.is_system_admin_only END,
	is_active = EXCLUDED.is_active,
	updated_at_utc = NOW()
-- Replays leave identical seed rows, and their updated_at_utc, untouched.
WHERE (system_tools.module_id, system_tools.label_key, system_tools.route, system_tools.sort_order,
		system_tools.is_system_admin_only, system_tools.is_active)
	IS DISTINCT FROM (EXCLUDED.module_id, EXCLUDED.label_key, EXCLUDED.route, EXCLUDED.sort_order,
		CASE WHEN system_tools.key IN ('plans', 'billing')
			THEN system_tools.is_system_admin_only ELSE EXCLUDED.is_system_admin_only END,
		EXCLUDED.is_active);
