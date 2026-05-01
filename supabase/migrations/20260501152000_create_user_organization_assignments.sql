CREATE TABLE IF NOT EXISTS public.user_organization_assignments (
    user_id UUID NOT NULL,
    organization_id UUID NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, organization_id),
    CONSTRAINT fk_user_org_assign_user
        FOREIGN KEY (user_id)
        REFERENCES public.users(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_user_org_assign_organization
        FOREIGN KEY (organization_id)
        REFERENCES public.organizations(id)
        ON DELETE CASCADE,
    CONSTRAINT ck_user_org_assign_sort_order CHECK (sort_order >= 0)
);

CREATE INDEX IF NOT EXISTS ix_user_org_assign_user_id
    ON public.user_organization_assignments(user_id);

CREATE INDEX IF NOT EXISTS ix_user_org_assign_organization_id
    ON public.user_organization_assignments(organization_id);

CREATE INDEX IF NOT EXISTS ix_user_org_assign_sort_order
    ON public.user_organization_assignments(user_id, sort_order);

ALTER TABLE public.user_organization_assignments ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.user_organization_assignments;
CREATE POLICY "Service role full access" ON public.user_organization_assignments
    TO service_role
    USING (true)
    WITH CHECK (true);
