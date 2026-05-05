-- Tournament modalities catalog (fixed, read-only for now)
CREATE TABLE IF NOT EXISTS public.tournament_modalities (
    id UUID PRIMARY KEY,
    name_es TEXT NOT NULL CHECK (char_length(name_es) BETWEEN 1 AND 120),
    name_en TEXT NOT NULL CHECK (char_length(name_en) BETWEEN 1 AND 120),
    name_pt TEXT NOT NULL CHECK (char_length(name_pt) BETWEEN 1 AND 120),
    key TEXT NOT NULL UNIQUE CHECK (char_length(key) BETWEEN 1 AND 80),
    sort_order INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_tournament_modalities_key ON public.tournament_modalities(key);
CREATE INDEX IF NOT EXISTS ix_tournament_modalities_active_sort ON public.tournament_modalities(is_active, sort_order);

-- Seed fixed modalities
INSERT INTO public.tournament_modalities (id, name_es, name_en, name_pt, key, sort_order, is_active)
VALUES
    ('550e8400-e29b-41d4-a716-446655440001', 'Individual', 'Single', 'Individual', 'single', 1, TRUE),
    ('550e8400-e29b-41d4-a716-446655440002', 'Parejas', 'Doubles', 'Duplas', 'doubles', 2, TRUE),
    ('550e8400-e29b-41d4-a716-446655440003', 'Equipos', 'Teams', 'Equipes', 'teams', 3, TRUE)
ON CONFLICT (key) DO UPDATE SET
    name_es = EXCLUDED.name_es,
    name_en = EXCLUDED.name_en,
    name_pt = EXCLUDED.name_pt,
    sort_order = EXCLUDED.sort_order,
    is_active = EXCLUDED.is_active,
    updated_at_utc = NOW();

ALTER TABLE public.tournament_modalities ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.tournament_modalities;
CREATE POLICY "Service role full access" ON public.tournament_modalities
    TO service_role
    USING (true)
    WITH CHECK (true);
