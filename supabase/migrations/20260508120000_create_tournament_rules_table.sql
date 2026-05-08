-- Tournament Rules: catalog of named rule sets that an admin attaches to a
-- tournament (e.g. "Padel doubles, best of 3, super tie-break"). Replaces the
-- previous JSON-driven `tournament_rule_sets` table that was bound to a
-- `tournament_type_id` we are dropping. Each row carries a single `name` plus
-- a multilingual description so the admin layer can show the right copy
-- regardless of the active locale.

CREATE TABLE IF NOT EXISTS public.tournament_rules (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 160),
    description_es TEXT NULL,
    description_en TEXT NULL,
    description_pt TEXT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0 CHECK (sort_order >= 0),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_tournament_rules_active_sort
    ON public.tournament_rules(is_active, sort_order);

ALTER TABLE public.tournament_rules ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.tournament_rules;
CREATE POLICY "Service role full access" ON public.tournament_rules
    TO service_role
    USING (true)
    WITH CHECK (true);

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournament_rules TO service_role;

-- Seed a couple of example rule sets so the FE selectors are not empty on
-- first boot. The rule_set_id column already exists on the tournaments table
-- (added in 20260507200000) so no schema change is needed there.
INSERT INTO public.tournament_rules (
    id, name, description_es, description_en, description_pt, sort_order, is_active)
VALUES
    ('22222222-2222-2222-2222-000000000001',
     'Best of 3 + super tie-break',
     'Tres sets, super tie-break a 10 puntos en el último set.',
     'Three sets, super tie-break to 10 points on the deciding set.',
     'Três sets, super tie-break até 10 pontos no set decisivo.',
     1, TRUE),
    ('22222222-2222-2222-2222-000000000002',
     'Pro Set 9 games',
     'Un set largo a 9 juegos con tie-break en 8-8.',
     'A pro set to 9 games with a tie-break at 8-8.',
     'Pro set até 9 games com tie-break em 8-8.',
     2, TRUE),
    ('22222222-2222-2222-2222-000000000003',
     'Round Robin + Knockout',
     'Fase de grupos seguida de eliminatoria directa.',
     'Group stage followed by single elimination knockout.',
     'Fase de grupos seguida de eliminatória direta.',
     3, TRUE)
ON CONFLICT (id) DO NOTHING;
