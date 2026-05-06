-- Canonical roles catalog and one-role-per-user association.

CREATE TABLE IF NOT EXISTS public.roles (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL CHECK (char_length(name) BETWEEN 3 AND 50),
    description TEXT NOT NULL CHECK (char_length(description) BETWEEN 3 AND 500),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    is_system BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_name ON public.roles(name);
CREATE INDEX IF NOT EXISTS ix_roles_is_active ON public.roles(is_active);

ALTER TABLE public.roles ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_policies
        WHERE schemaname = 'public'
          AND tablename = 'roles'
          AND policyname = 'Service role full access on roles'
    ) THEN
        CREATE POLICY "Service role full access on roles" ON public.roles
            TO service_role
            USING (true)
            WITH CHECK (true);
    END IF;
END $$;

INSERT INTO public.roles (id, name, description, is_active, is_system, created_at_utc, updated_at_utc)
VALUES
    ('role001', 'system_admin', 'Full platform access and governance permissions', TRUE, TRUE, NOW(), NOW()),
    ('role002', 'admin', 'Tenant administration and operational management permissions', TRUE, TRUE, NOW(), NOW()),
    ('role003', 'viewer', 'Read-only access to administrative data and reports', TRUE, TRUE, NOW(), NOW()),
    ('role004', 'editor', 'Operational editing permissions without tenant governance access', TRUE, TRUE, NOW(), NOW()),
    ('role005', 'user', 'Standard authenticated user role', TRUE, TRUE, NOW(), NOW()),
    ('role006', 'player', 'Player-facing role for tournament and registration flows', TRUE, TRUE, NOW(), NOW())
ON CONFLICT (id) DO UPDATE
SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    is_active = EXCLUDED.is_active,
    is_system = EXCLUDED.is_system,
    updated_at_utc = NOW();

ALTER TABLE public.users
    ADD COLUMN IF NOT EXISTS role_id TEXT;

UPDATE public.users
SET role_id = CASE lower(role)
    WHEN 'system_admin' THEN 'role001'
    WHEN 'admin' THEN 'role002'
    WHEN 'viewer' THEN 'role003'
    WHEN 'editor' THEN 'role004'
    WHEN 'user' THEN 'role005'
    WHEN 'player' THEN 'role006'
    ELSE 'role003'
END
WHERE role_id IS NULL OR role_id = '';

ALTER TABLE public.users
    ALTER COLUMN role_id SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_users_role'
          AND conrelid = 'public.users'::regclass
    ) THEN
        ALTER TABLE public.users
            ADD CONSTRAINT fk_users_role
            FOREIGN KEY (role_id)
            REFERENCES public.roles(id)
            ON UPDATE CASCADE
            ON DELETE RESTRICT;
    END IF;
END $$;

ALTER TABLE public.users DROP CONSTRAINT IF EXISTS users_role_check;

ALTER TABLE public.users
    ADD CONSTRAINT users_role_check
    CHECK (role IN ('system_admin', 'admin', 'editor', 'user', 'player', 'viewer'));

CREATE INDEX IF NOT EXISTS ix_users_role_id ON public.users(role_id);
