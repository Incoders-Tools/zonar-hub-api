CREATE TABLE IF NOT EXISTS public.registrations (
    id UUID PRIMARY KEY,
    organization_id UUID NOT NULL,
    tournament_id UUID NULL,
    player_id UUID NOT NULL,
    status TEXT NOT NULL DEFAULT 'confirmed' CHECK (status IN ('pending', 'confirmed', 'cancelled')),
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_registrations_organization
        FOREIGN KEY (organization_id)
        REFERENCES public.organizations(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_registrations_tournament
        FOREIGN KEY (tournament_id)
        REFERENCES public.tournaments(id)
        ON DELETE SET NULL,
    CONSTRAINT fk_registrations_player
        FOREIGN KEY (player_id)
        REFERENCES public.users(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_registrations_organization_id
    ON public.registrations(organization_id);

CREATE INDEX IF NOT EXISTS ix_registrations_tournament_id
    ON public.registrations(tournament_id);

CREATE INDEX IF NOT EXISTS ix_registrations_player_id
    ON public.registrations(player_id);

ALTER TABLE public.registrations ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.registrations;
CREATE POLICY "Service role full access" ON public.registrations
    TO service_role
    USING (true)
    WITH CHECK (true);
