-- Remove duplicate "Organizaciones" entry from the permission catalog.
-- The tools `tenants` and `organizations` were both surfaced under the
-- system module with the same translated label ("Organizaciones" / "Organizations" / "Organizações"),
-- producing a duplicated row in the permission matrix.
--
-- The user-facing concept is "organizations"; "tenants" was kept as an
-- internal alias and is no longer exposed in navigation. Drop the tool so
-- the catalog and any user assignments stay coherent.

DELETE FROM public.user_organization_permissions
WHERE tool_key = 'tenants';

DELETE FROM public.system_tools
WHERE key = 'tenants';
