-- Promote tournament statuses from a JSON system_setting to a real table
-- with multilingual columns (ES/EN/PT). Drop the old setting so the
-- application falls back exclusively to the new source of truth.

CREATE TABLE IF NOT EXISTS public.tournament_statuses (
    id UUID PRIMARY KEY,
    key TEXT NOT NULL UNIQUE CHECK (char_length(key) BETWEEN 1 AND 80),
    name_es TEXT NOT NULL CHECK (char_length(name_es) BETWEEN 1 AND 120),
    name_en TEXT NOT NULL CHECK (char_length(name_en) BETWEEN 1 AND 120),
    name_pt TEXT NOT NULL CHECK (char_length(name_pt) BETWEEN 1 AND 120),
    description_es TEXT NULL,
    description_en TEXT NULL,
    description_pt TEXT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0 CHECK (sort_order >= 0),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_tournament_statuses_active_sort
    ON public.tournament_statuses(is_active, sort_order);

ALTER TABLE public.tournament_statuses ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.tournament_statuses;
CREATE POLICY "Service role full access" ON public.tournament_statuses
    TO service_role
    USING (true)
    WITH CHECK (true);

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournament_statuses TO service_role;

INSERT INTO public.tournament_statuses (
    id, key, name_es, name_en, name_pt,
    description_es, description_en, description_pt, sort_order, is_active)
VALUES
    ('11111111-1111-1111-1111-000000000001',
     'registration_open',
     'Inscripción abierta', 'Registration open', 'Inscrição aberta',
     'Período de inscripción activo', 'Registration period active', 'Período de inscrição ativo',
     1, TRUE),
    ('11111111-1111-1111-1111-000000000002',
     'in_progress',
     'En curso', 'In progress', 'Em andamento',
     'Torneo en progreso', 'Tournament in progress', 'Torneio em andamento',
     2, TRUE),
    ('11111111-1111-1111-1111-000000000003',
     'finished',
     'Finalizado', 'Finished', 'Finalizado',
     'Torneo completado', 'Tournament completed', 'Torneio concluído',
     3, TRUE),
    ('11111111-1111-1111-1111-000000000004',
     'cancelled',
     'Cancelado', 'Cancelled', 'Cancelado',
     'Torneo cancelado', 'Tournament cancelled', 'Torneio cancelado',
     4, TRUE)
ON CONFLICT (key) DO UPDATE SET
    name_es = EXCLUDED.name_es,
    name_en = EXCLUDED.name_en,
    name_pt = EXCLUDED.name_pt,
    description_es = EXCLUDED.description_es,
    description_en = EXCLUDED.description_en,
    description_pt = EXCLUDED.description_pt,
    sort_order = EXCLUDED.sort_order,
    is_active = EXCLUDED.is_active,
    updated_at_utc = NOW();

-- Drop the legacy JSON-backed setting so old data no longer leaks into
-- the application after the migration runs.
DELETE FROM public.system_settings
WHERE key = 'catalog.tournament_statuses';
