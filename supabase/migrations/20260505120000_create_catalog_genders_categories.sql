-- Catalog tables: genders and categories used across the application

-- Gender catalog (Caballeros, Damas, Mixto)
CREATE TABLE IF NOT EXISTS public.genders (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 100),
    key TEXT NOT NULL UNIQUE CHECK (char_length(key) BETWEEN 1 AND 50),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_genders_key ON public.genders(key);
CREATE INDEX IF NOT EXISTS ix_genders_sort ON public.genders(sort_order);

ALTER TABLE public.genders ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.genders;
CREATE POLICY "Service role full access" ON public.genders
    TO service_role
    USING (true)
    WITH CHECK (true);

INSERT INTO public.genders (id, name, key, is_active, sort_order)
VALUES
    ('a1000000-0000-0000-0000-000000000001', 'Caballeros', 'male',   true, 1),
    ('a1000000-0000-0000-0000-000000000002', 'Damas',      'female', true, 2),
    ('a1000000-0000-0000-0000-000000000003', 'Mixto',      'mixed',  true, 3)
ON CONFLICT (key) DO UPDATE SET
    name       = EXCLUDED.name,
    sort_order = EXCLUDED.sort_order,
    is_active  = EXCLUDED.is_active,
    updated_at_utc = NOW();

-- Category catalog (1ª–7ª Categoría + Promocional)
CREATE TABLE IF NOT EXISTS public.categories (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL CHECK (char_length(name) BETWEEN 1 AND 120),
    short_name TEXT NOT NULL CHECK (char_length(short_name) BETWEEN 1 AND 30),
    key TEXT NOT NULL UNIQUE CHECK (char_length(key) BETWEEN 1 AND 60),
    level INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_categories_key ON public.categories(key);
CREATE INDEX IF NOT EXISTS ix_categories_sort ON public.categories(sort_order);

ALTER TABLE public.categories ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Service role full access" ON public.categories;
CREATE POLICY "Service role full access" ON public.categories
    TO service_role
    USING (true)
    WITH CHECK (true);

INSERT INTO public.categories (id, name, short_name, key, level, is_active, sort_order)
VALUES
    ('b1000000-0000-0000-0000-000000000001', '1ª Categoría', '1ª',   'first',        1, true, 1),
    ('b1000000-0000-0000-0000-000000000002', '2ª Categoría', '2ª',   'second',       2, true, 2),
    ('b1000000-0000-0000-0000-000000000003', '3ª Categoría', '3ª',   'third',        3, true, 3),
    ('b1000000-0000-0000-0000-000000000004', '4ª Categoría', '4ª',   'fourth',       4, true, 4),
    ('b1000000-0000-0000-0000-000000000005', '5ª Categoría', '5ª',   'fifth',        5, true, 5),
    ('b1000000-0000-0000-0000-000000000006', '6ª Categoría', '6ª',   'sixth',        6, true, 6),
    ('b1000000-0000-0000-0000-000000000007', '7ª Categoría', '7ª',   'seventh',      7, true, 7),
    ('b1000000-0000-0000-0000-000000000008', 'Promocional',  'Promo','promotional',  8, false, 8)
ON CONFLICT (key) DO UPDATE SET
    name       = EXCLUDED.name,
    short_name = EXCLUDED.short_name,
    level      = EXCLUDED.level,
    is_active  = EXCLUDED.is_active,
    sort_order = EXCLUDED.sort_order,
    updated_at_utc = NOW();
