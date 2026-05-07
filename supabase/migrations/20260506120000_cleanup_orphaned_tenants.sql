-- Cleanup orphaned tenants: tenants that were created during a failed
-- registration attempt (tenant INSERT succeeded but user INSERT failed).
-- These records block future registration attempts with the same email
-- because of the unique constraint on contact_email.

DELETE FROM public.tenants
WHERE id NOT IN (
    SELECT DISTINCT tenant_id
    FROM public.users
    WHERE tenant_id IS NOT NULL
);
