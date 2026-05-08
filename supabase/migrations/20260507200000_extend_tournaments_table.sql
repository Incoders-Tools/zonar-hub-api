-- Extend the tournaments table with the persistence surface the admin
-- frontend uses (registration window, modality, max participants, optional
-- description / rules / fees / cover image / etc.). Every new column is
-- nullable so existing rows stay valid.

ALTER TABLE public.tournaments
    ADD COLUMN IF NOT EXISTS key TEXT NULL,
    ADD COLUMN IF NOT EXISTS category_id UUID NULL,
    ADD COLUMN IF NOT EXISTS gender_id UUID NULL,
    ADD COLUMN IF NOT EXISTS modality_id UUID NULL,
    ADD COLUMN IF NOT EXISTS tournament_type_id UUID NULL,
    ADD COLUMN IF NOT EXISTS rule_set_id UUID NULL,
    ADD COLUMN IF NOT EXISTS registration_start_date DATE NULL,
    ADD COLUMN IF NOT EXISTS registration_end_date DATE NULL,
    ADD COLUMN IF NOT EXISTS max_pairs INTEGER NULL,
    ADD COLUMN IF NOT EXISTS description TEXT NULL,
    ADD COLUMN IF NOT EXISTS rules TEXT NULL,
    ADD COLUMN IF NOT EXISTS image_url TEXT NULL,
    ADD COLUMN IF NOT EXISTS cover_image_url TEXT NULL,
    ADD COLUMN IF NOT EXISTS registration_fee_per_pair NUMERIC(12, 2) NULL,
    ADD COLUMN IF NOT EXISTS prize_money NUMERIC(12, 2) NULL,
    ADD COLUMN IF NOT EXISTS points_to_award INTEGER NULL,
    ADD COLUMN IF NOT EXISTS sum_value NUMERIC(12, 2) NULL,
    ADD COLUMN IF NOT EXISTS observations TEXT NULL,
    ADD COLUMN IF NOT EXISTS selected_court_ids JSONB NOT NULL DEFAULT '[]'::jsonb;

-- Status was constrained to a small enum. The frontend-driven status keys
-- (registration_open, in_progress, finished, cancelled, etc.) are stored
-- as free text now that the canonical catalog lives in tournament_statuses.
ALTER TABLE public.tournaments
    DROP CONSTRAINT IF EXISTS tournaments_status_check;

ALTER TABLE public.tournaments
    ALTER COLUMN status DROP NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_tournaments_org_key
    ON public.tournaments(organization_id, key)
    WHERE key IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_tournaments_modality
    ON public.tournaments(modality_id);

CREATE INDEX IF NOT EXISTS ix_tournaments_category
    ON public.tournaments(category_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournaments TO service_role;
