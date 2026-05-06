-- Extend complexes table with additional fields required by the frontend form

ALTER TABLE public.complexes
    ADD COLUMN IF NOT EXISTS key TEXT NULL CHECK (key IS NULL OR char_length(key) BETWEEN 1 AND 100),
    ADD COLUMN IF NOT EXISTS sort_order INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS preponderance INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS description TEXT NULL CHECK (description IS NULL OR char_length(description) <= 500),
    ADD COLUMN IF NOT EXISTS logo_image_path TEXT NULL,
    ADD COLUMN IF NOT EXISTS cover_image_path TEXT NULL,
    ADD COLUMN IF NOT EXISTS layout_diagram_path TEXT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ix_complexes_key_org ON public.complexes(organization_id, key)
    WHERE key IS NOT NULL;
