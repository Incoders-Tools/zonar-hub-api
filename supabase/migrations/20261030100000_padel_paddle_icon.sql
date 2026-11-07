-- Padel renders the paddle drawing (public/uploads/sports-icons/padel.svg in the frontend) instead of
-- the tennis-ball emoji it shared with tennis. Only the seeded value is replaced: a padel icon that an
-- administrator already customised is left untouched. Data-only change; schema and grants are kept.
UPDATE public.sports
SET
	icon = 'padel',
	icon_source = 'svg',
	updated_at_utc = NOW()
WHERE key = 'padel'
	AND icon = '🎾'
	AND icon_source = 'unicode';
