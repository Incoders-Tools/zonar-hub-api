-- Public tournament showcase (phase 1). Additive only: no existing row is rewritten.
--
-- tournaments: an organization decides whether a tournament is shown publicly. The API generates
-- public_slug (<slugified-name>-<6 base36 chars>, see TournamentPublicSlug) the first time a tournament is
-- saved public and never regenerates it, so the share link survives turning visibility off and on.
--
-- organizations: consent to appear in ZonarHub public sections (public_profile_enabled) and the optional
-- contact links shown next to the organization's public tournaments. The API validates the formats
-- (absolute http/https URLs, email, + and 8-16 digits); the database only bounds the lengths.

ALTER TABLE public.tournaments
    ADD COLUMN IF NOT EXISTS is_public BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS public_slug TEXT NULL CHECK (
        public_slug IS NULL OR (
            char_length(public_slug) BETWEEN 3 AND 80
            AND public_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'
        )
    );

CREATE UNIQUE INDEX IF NOT EXISTS ux_tournaments_public_slug
    ON public.tournaments(public_slug)
    WHERE public_slug IS NOT NULL;

ALTER TABLE public.organizations
    ADD COLUMN IF NOT EXISTS public_profile_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS website_url TEXT NULL CHECK (website_url IS NULL OR char_length(website_url) <= 500),
    ADD COLUMN IF NOT EXISTS instagram_url TEXT NULL CHECK (instagram_url IS NULL OR char_length(instagram_url) <= 500),
    ADD COLUMN IF NOT EXISTS facebook_url TEXT NULL CHECK (facebook_url IS NULL OR char_length(facebook_url) <= 500),
    ADD COLUMN IF NOT EXISTS tiktok_url TEXT NULL CHECK (tiktok_url IS NULL OR char_length(tiktok_url) <= 500),
    ADD COLUMN IF NOT EXISTS youtube_url TEXT NULL CHECK (youtube_url IS NULL OR char_length(youtube_url) <= 500),
    ADD COLUMN IF NOT EXISTS whatsapp_phone TEXT NULL CHECK (whatsapp_phone IS NULL OR char_length(whatsapp_phone) <= 17),
    ADD COLUMN IF NOT EXISTS contact_email TEXT NULL CHECK (contact_email IS NULL OR char_length(contact_email) <= 254);

-- The API reaches both tables through the service role; table-level privileges cover the new columns.
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournaments TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.organizations TO service_role;
