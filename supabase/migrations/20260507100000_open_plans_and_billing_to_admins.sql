-- Plans and Billing belong to the operator-facing surface of the System
-- module. Admins running their own organization need to see them in order to
-- check / change their plan and review billing, even if the tooling is
-- ultimately operated through commercial integrations.
--
-- Drop the system_admin_only flag for both tools so the role-based default
-- permissions surface them for Admin users.

UPDATE public.system_tools
SET is_system_admin_only = FALSE,
    updated_at_utc = NOW()
WHERE key IN ('plans', 'billing')
  -- Replays leave already-open tools, and their updated_at_utc, untouched.
  AND is_system_admin_only;
