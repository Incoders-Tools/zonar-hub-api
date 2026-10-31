-- Tournament draws and match results (showcase phase 2, D1/D2). Additive only: two new tables, no existing row is
-- rewritten. Plain CREATE (no IF NOT EXISTS) so a reused name fails instead of silently keeping a different shape.
--
-- tournament_draws: at most one draw per tournament (D1), private while 'draft', public when 'published'. The API
-- (TournamentDrawRepository) writes every column; organization_id is copied from the tournament for tenant scoping.
-- structure is the generated draw snapshot and the source of truth for the bracket shape: it is never regenerated
-- from the seed after saving. options, request and structure are versioned documents ("version": 1, see
-- TournamentDrawJson). seed is text because the engine seed is an unsigned 64-bit integer that does not fit bigint.
--
-- tournament_draw_match_results: one row per draw match (match_id is the draw-local id, e.g. KO-R1-M3); sets is
-- [{ "home", "away", "tiebreak": { "home", "away" } | null }]. Score rules (D3/D4) are validated by the domain.
--
-- Enum values are stored with the camelCase spelling of the API contracts (DrawWireNames): status 'draft' |
-- 'published'; format 'singleElimination' | 'roundRobin' | 'groupsThenKnockout'; participation_format 'individual' |
-- 'pair' | 'team'; randomization_mode 'random' | 'seeded'; match status 'scheduled' | 'inProgress' | 'completed' |
-- 'walkover' | 'retired'; winner_side 'home' | 'away'. Timestamps are timestamptz in UTC with the _utc suffix, and
-- updated_at_utc is set by the API on every write (no trigger), as on the other tables.

CREATE TABLE public.tournament_draws (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tournament_id UUID NOT NULL,
    organization_id UUID NOT NULL,
    status TEXT NOT NULL DEFAULT 'draft',
    format TEXT NOT NULL,
    participation_format TEXT NOT NULL,
    randomization_mode TEXT NOT NULL,
    seed TEXT NOT NULL,
    options JSONB NOT NULL,
    request JSONB NOT NULL,
    structure JSONB NOT NULL,
    published_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_tournament_draws_tournament
        FOREIGN KEY (tournament_id)
        REFERENCES public.tournaments(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_tournament_draws_organization
        FOREIGN KEY (organization_id)
        REFERENCES public.organizations(id)
        ON DELETE CASCADE,
    CONSTRAINT ux_tournament_draws_tournament UNIQUE (tournament_id),
    CONSTRAINT ck_tournament_draws_status CHECK (status IN ('draft', 'published')),
    CONSTRAINT ck_tournament_draws_published_at CHECK ((status = 'published') = (published_at_utc IS NOT NULL)),
    CONSTRAINT ck_tournament_draws_format
        CHECK (format IN ('singleElimination', 'roundRobin', 'groupsThenKnockout')),
    CONSTRAINT ck_tournament_draws_participation_format
        CHECK (participation_format IN ('individual', 'pair', 'team')),
    CONSTRAINT ck_tournament_draws_randomization_mode CHECK (randomization_mode IN ('random', 'seeded')),
    CONSTRAINT ck_tournament_draws_seed CHECK (seed ~ '^[0-9]{1,20}$'),
    CONSTRAINT ck_tournament_draws_options
        CHECK (jsonb_typeof(options) = 'object' AND jsonb_typeof(options -> 'version') = 'number'),
    CONSTRAINT ck_tournament_draws_request
        CHECK (jsonb_typeof(request) = 'object' AND jsonb_typeof(request -> 'version') = 'number'),
    CONSTRAINT ck_tournament_draws_structure
        CHECK (jsonb_typeof(structure) = 'object' AND jsonb_typeof(structure -> 'version') = 'number')
);

-- Lookups by tournament use ux_tournament_draws_tournament; this one serves tenant-wide queries and the FK.
CREATE INDEX ix_tournament_draws_organization ON public.tournament_draws(organization_id);

CREATE TABLE public.tournament_draw_match_results (
    draw_id UUID NOT NULL,
    match_id TEXT NOT NULL,
    status TEXT NOT NULL,
    sets JSONB NOT NULL DEFAULT '[]'::jsonb,
    winner_side TEXT NULL,
    court_id UUID NULL,
    court_name TEXT NULL,
    scheduled_at_utc TIMESTAMPTZ NULL,
    started_at_utc TIMESTAMPTZ NULL,
    finished_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    -- Unique draw and match; its leading draw_id also serves every lookup by draw.
    CONSTRAINT pk_tournament_draw_match_results PRIMARY KEY (draw_id, match_id),
    CONSTRAINT fk_tournament_draw_match_results_draw
        FOREIGN KEY (draw_id)
        REFERENCES public.tournament_draws(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_tournament_draw_match_results_court
        FOREIGN KEY (court_id)
        REFERENCES public.courts(id)
        ON DELETE SET NULL,
    CONSTRAINT ck_tournament_draw_match_results_match_id
        CHECK (char_length(match_id) <= 64 AND match_id ~ '^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$'),
    CONSTRAINT ck_tournament_draw_match_results_status
        CHECK (status IN ('scheduled', 'inProgress', 'completed', 'walkover', 'retired')),
    CONSTRAINT ck_tournament_draw_match_results_winner_side
        CHECK (winner_side IS NULL OR winner_side IN ('home', 'away')),
    -- Decided statuses carry a winner; the others never do (MatchResultValidator enforces the same rule).
    CONSTRAINT ck_tournament_draw_match_results_decided
        CHECK ((status IN ('completed', 'walkover', 'retired')) = (winner_side IS NOT NULL)),
    CONSTRAINT ck_tournament_draw_match_results_sets CHECK (jsonb_typeof(sets) = 'array'),
    CONSTRAINT ck_tournament_draw_match_results_court_name
        CHECK (court_name IS NULL OR char_length(court_name) BETWEEN 1 AND 200),
    CONSTRAINT ck_tournament_draw_match_results_times
        CHECK (finished_at_utc IS NULL OR started_at_utc IS NULL OR finished_at_utc >= started_at_utc)
);

-- Serves ON DELETE SET NULL when a court is removed.
CREATE INDEX ix_tournament_draw_match_results_court
    ON public.tournament_draw_match_results(court_id)
    WHERE court_id IS NOT NULL;

ALTER TABLE public.tournament_draws ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.tournament_draw_match_results ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Service role full access" ON public.tournament_draws
    TO service_role
    USING (true)
    WITH CHECK (true);

CREATE POLICY "Service role full access" ON public.tournament_draw_match_results
    TO service_role
    USING (true)
    WITH CHECK (true);

-- Hosted default privileges grant new public tables to every API role, and RLS does not govern TRUNCATE; drop them
-- and keep only plain DML for the service role the API uses. Anonymous reads go through the API, never PostgREST.
REVOKE ALL ON TABLE public.tournament_draws FROM PUBLIC, anon, authenticated, service_role;
REVOKE ALL ON TABLE public.tournament_draw_match_results FROM PUBLIC, anon, authenticated, service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournament_draws TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournament_draw_match_results TO service_role;
