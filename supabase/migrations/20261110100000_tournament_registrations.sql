-- Players, eligibility profiles and tournament registrations (registrations feature R1: S3, S4, D1, D2). Additive only:
-- five new tables, one nullable column on tournaments and five functions; the legacy public.registrations table (one
-- row per user) is neither changed nor dropped. Plain CREATE / ADD COLUMN (no IF NOT EXISTS, no OR REPLACE) so a reused
-- name fails instead of silently keeping a different shape.
--
-- players: the organization's own player records (S3), optionally linked to a user. Email is stored trimmed and
-- lowercase (the API normalizes it) and is unique per organization when present, as are the document and the linked
-- user. The API maps the unique indexes ux_players_organization_email / _document / _user to players.* error codes.
--
-- tournament_eligibility_profiles (+ _slots): reusable per-organization participant rules, one slot per participant;
-- tournaments.eligibility_profile_id references one (same organization, checked by the API). A referenced profile
-- cannot be deleted (ON DELETE RESTRICT), so a tournament never silently loses its rules.
--
-- tournament_registrations (+ _participants): one registration per pair/team (S4). Status values and transitions are
-- D2: 'pending' -> 'confirmed' | 'rejected' | 'waitlisted' | 'cancelled'; 'waitlisted' -> 'confirmed' | 'cancelled';
-- 'confirmed' -> 'cancelled'; 'rejected' and 'cancelled' are final. Source is 'public' | 'admin'; payment_status is
-- 'not_required' | 'pending' | 'paid' | 'refunded' | 'failed'. revision is incremented by every write (optimistic
-- concurrency, as tournament_draws).
--
-- One active registration per player per tournament is declarative: is_active is a stored generated column (status
-- not final) and every participant row copies (registration_id, tournament_id, registration_active) through a composite
-- foreign key with ON UPDATE CASCADE, so a status change propagates to its participants in the same statement; the
-- partial unique index ux_tournament_registration_participants_active_player then allows a player in at most one
-- active registration of a tournament, whatever path writes the rows.
--
-- Writes that must be atomic go through the functions below (draw revision precedent: SECURITY INVOKER, fixed
-- search_path, EXECUTE for service_role only, timestamps from the API clock). Registration writers lock the tournament
-- row (FOR NO KEY UPDATE, which does not block foreign-key inserts elsewhere) so capacity counts are serialized per
-- tournament. Capacity is tournaments.max_pairs over 'confirmed'; null or a value below 1 means no limit. A refused
-- write raises SQLSTATE PT409 (PostgREST answers HTTP 409) with a stable message the API maps (RegistrationErrors):
--   registrations.tournament_not_found       the tournament does not exist
--   registrations.player_not_found           a participant is not a player of the tournament's organization
--   registrations.player_already_registered  a participant already has an active registration in the tournament
--   registrations.capacity_reached           confirming (or creating confirmed) beyond max_pairs
--   registrations.invalid_transition         the D2 table does not allow the status change
--   registrations.concurrent_modification    the registration is not at the expected revision or no longer exists
--   eligibility_profiles.not_found           the profile id belongs to another organization
-- Invalid arguments raise plain exceptions ('invalid ... arguments'); check constraints keep their own SQLSTATE.
-- The functions do not authorize users: the API applies the tenant rules before calling them.

CREATE TABLE public.players (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL,
    user_id UUID NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    email TEXT NULL,
    phone TEXT NULL,
    document_id TEXT NULL,
    birth_date DATE NULL,
    gender_id UUID NULL,
    category_id UUID NULL,
    club TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_players_organization
        FOREIGN KEY (organization_id) REFERENCES public.organizations(id) ON DELETE CASCADE,
    CONSTRAINT fk_players_user
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE SET NULL,
    CONSTRAINT fk_players_gender
        FOREIGN KEY (gender_id) REFERENCES public.genders(id) ON DELETE SET NULL,
    CONSTRAINT fk_players_category
        FOREIGN KEY (category_id) REFERENCES public.categories(id) ON DELETE SET NULL,
    CONSTRAINT ck_players_first_name CHECK (char_length(first_name) BETWEEN 1 AND 100),
    CONSTRAINT ck_players_last_name CHECK (char_length(last_name) BETWEEN 1 AND 100),
    CONSTRAINT ck_players_email
        CHECK (email IS NULL OR (char_length(email) BETWEEN 3 AND 254 AND email = lower(btrim(email)) AND email LIKE '_%@_%')),
    CONSTRAINT ck_players_phone CHECK (phone IS NULL OR char_length(phone) BETWEEN 1 AND 40),
    CONSTRAINT ck_players_document_id CHECK (document_id IS NULL OR char_length(document_id) BETWEEN 1 AND 40),
    CONSTRAINT ck_players_club CHECK (club IS NULL OR char_length(club) BETWEEN 1 AND 120)
);

CREATE UNIQUE INDEX ux_players_organization_email
    ON public.players(organization_id, lower(email)) WHERE email IS NOT NULL;
CREATE UNIQUE INDEX ux_players_organization_document
    ON public.players(organization_id, document_id) WHERE document_id IS NOT NULL;
CREATE UNIQUE INDEX ux_players_organization_user
    ON public.players(organization_id, user_id) WHERE user_id IS NOT NULL;
-- Organization listing order (last name, first name); also serves the organization foreign key.
CREATE INDEX ix_players_organization_name ON public.players(organization_id, last_name, first_name);
CREATE INDEX ix_players_user ON public.players(user_id) WHERE user_id IS NOT NULL;

CREATE TABLE public.tournament_eligibility_profiles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID NOT NULL,
    name TEXT NOT NULL,
    description TEXT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_tournament_eligibility_profiles_organization
        FOREIGN KEY (organization_id) REFERENCES public.organizations(id) ON DELETE CASCADE,
    CONSTRAINT ck_tournament_eligibility_profiles_name CHECK (char_length(name) BETWEEN 1 AND 120),
    CONSTRAINT ck_tournament_eligibility_profiles_description
        CHECK (description IS NULL OR char_length(description) BETWEEN 1 AND 500)
);

-- Unique name per organization; its leading organization_id also serves listings and the foreign key.
CREATE UNIQUE INDEX ux_tournament_eligibility_profiles_organization_name
    ON public.tournament_eligibility_profiles(organization_id, lower(name));

CREATE TABLE public.tournament_eligibility_profile_slots (
    profile_id UUID NOT NULL,
    slot_number INTEGER NOT NULL,
    gender_id UUID NULL,
    category_id UUID NULL,
    min_age INTEGER NULL,
    max_age INTEGER NULL,
    label TEXT NULL,
    required BOOLEAN NOT NULL DEFAULT TRUE,
    CONSTRAINT pk_tournament_eligibility_profile_slots PRIMARY KEY (profile_id, slot_number),
    CONSTRAINT fk_tournament_eligibility_profile_slots_profile
        FOREIGN KEY (profile_id) REFERENCES public.tournament_eligibility_profiles(id) ON DELETE CASCADE,
    CONSTRAINT fk_tournament_eligibility_profile_slots_gender
        FOREIGN KEY (gender_id) REFERENCES public.genders(id) ON DELETE SET NULL,
    CONSTRAINT fk_tournament_eligibility_profile_slots_category
        FOREIGN KEY (category_id) REFERENCES public.categories(id) ON DELETE SET NULL,
    CONSTRAINT ck_tournament_eligibility_profile_slots_slot_number CHECK (slot_number BETWEEN 1 AND 16),
    CONSTRAINT ck_tournament_eligibility_profile_slots_ages
        CHECK ((min_age IS NULL OR min_age BETWEEN 0 AND 120)
           AND (max_age IS NULL OR max_age BETWEEN 0 AND 120)
           AND (min_age IS NULL OR max_age IS NULL OR min_age <= max_age)),
    CONSTRAINT ck_tournament_eligibility_profile_slots_label CHECK (label IS NULL OR char_length(label) BETWEEN 1 AND 60)
);

ALTER TABLE public.tournaments
    ADD COLUMN eligibility_profile_id UUID NULL,
    ADD CONSTRAINT fk_tournaments_eligibility_profile
        FOREIGN KEY (eligibility_profile_id) REFERENCES public.tournament_eligibility_profiles(id) ON DELETE RESTRICT;

-- Serves the RESTRICT check when a profile is deleted.
CREATE INDEX ix_tournaments_eligibility_profile
    ON public.tournaments(eligibility_profile_id) WHERE eligibility_profile_id IS NOT NULL;

CREATE TABLE public.tournament_registrations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tournament_id UUID NOT NULL,
    organization_id UUID NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    source TEXT NOT NULL,
    payment_status TEXT NOT NULL,
    contact_email TEXT NULL,
    notes TEXT NULL,
    public_name_consent BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN GENERATED ALWAYS AS (status NOT IN ('rejected', 'cancelled')) STORED,
    revision INTEGER NOT NULL DEFAULT 0,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_tournament_registrations_tournament
        FOREIGN KEY (tournament_id) REFERENCES public.tournaments(id) ON DELETE CASCADE,
    CONSTRAINT fk_tournament_registrations_organization
        FOREIGN KEY (organization_id) REFERENCES public.organizations(id) ON DELETE CASCADE,
    -- Target of the participants' composite foreign key (see the header).
    CONSTRAINT ux_tournament_registrations_participant_key UNIQUE (id, tournament_id, is_active),
    CONSTRAINT ck_tournament_registrations_status
        CHECK (status IN ('pending', 'confirmed', 'rejected', 'waitlisted', 'cancelled')),
    CONSTRAINT ck_tournament_registrations_source CHECK (source IN ('public', 'admin')),
    CONSTRAINT ck_tournament_registrations_payment_status
        CHECK (payment_status IN ('not_required', 'pending', 'paid', 'refunded', 'failed')),
    CONSTRAINT ck_tournament_registrations_contact_email
        CHECK (contact_email IS NULL
            OR (char_length(contact_email) BETWEEN 3 AND 254 AND contact_email = lower(btrim(contact_email))
                AND contact_email LIKE '_%@_%')),
    CONSTRAINT ck_tournament_registrations_notes CHECK (notes IS NULL OR char_length(notes) BETWEEN 1 AND 2000),
    CONSTRAINT ck_tournament_registrations_revision CHECK (revision >= 0)
);

-- Listing and capacity counts by tournament and status; the organization index serves the dashboard count.
CREATE INDEX ix_tournament_registrations_tournament_status ON public.tournament_registrations(tournament_id, status);
CREATE INDEX ix_tournament_registrations_organization ON public.tournament_registrations(organization_id, is_active);

CREATE TABLE public.tournament_registration_participants (
    registration_id UUID NOT NULL,
    tournament_id UUID NOT NULL,
    registration_active BOOLEAN NOT NULL,
    slot_number INTEGER NOT NULL,
    player_id UUID NOT NULL,
    CONSTRAINT pk_tournament_registration_participants PRIMARY KEY (registration_id, slot_number),
    CONSTRAINT fk_tournament_registration_participants_registration
        FOREIGN KEY (registration_id, tournament_id, registration_active)
        REFERENCES public.tournament_registrations(id, tournament_id, is_active)
        ON UPDATE CASCADE
        ON DELETE CASCADE,
    -- A player with registrations cannot be deleted; deactivate it instead.
    CONSTRAINT fk_tournament_registration_participants_player
        FOREIGN KEY (player_id) REFERENCES public.players(id) ON DELETE RESTRICT,
    CONSTRAINT ux_tournament_registration_participants_player UNIQUE (registration_id, player_id),
    CONSTRAINT ck_tournament_registration_participants_slot_number CHECK (slot_number BETWEEN 1 AND 16)
);

CREATE UNIQUE INDEX ux_tournament_registration_participants_active_player
    ON public.tournament_registration_participants(tournament_id, player_id) WHERE registration_active;
-- Serves the player RESTRICT check and "registrations of a player" lookups.
CREATE INDEX ix_tournament_registration_participants_player ON public.tournament_registration_participants(player_id);

ALTER TABLE public.players ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.tournament_eligibility_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.tournament_eligibility_profile_slots ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.tournament_registrations ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.tournament_registration_participants ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Service role full access" ON public.players TO service_role USING (true) WITH CHECK (true);
CREATE POLICY "Service role full access" ON public.tournament_eligibility_profiles TO service_role USING (true) WITH CHECK (true);
CREATE POLICY "Service role full access" ON public.tournament_eligibility_profile_slots TO service_role USING (true) WITH CHECK (true);
CREATE POLICY "Service role full access" ON public.tournament_registrations TO service_role USING (true) WITH CHECK (true);
CREATE POLICY "Service role full access" ON public.tournament_registration_participants TO service_role USING (true) WITH CHECK (true);

-- Hosted default privileges grant new public tables to every API role, and RLS does not govern TRUNCATE; drop them
-- and keep only plain DML for the service role the API uses. Anonymous reads go through the API, never PostgREST.
REVOKE ALL ON TABLE public.players FROM PUBLIC, anon, authenticated, service_role;
REVOKE ALL ON TABLE public.tournament_eligibility_profiles FROM PUBLIC, anon, authenticated, service_role;
REVOKE ALL ON TABLE public.tournament_eligibility_profile_slots FROM PUBLIC, anon, authenticated, service_role;
REVOKE ALL ON TABLE public.tournament_registrations FROM PUBLIC, anon, authenticated, service_role;
REVOKE ALL ON TABLE public.tournament_registration_participants FROM PUBLIC, anon, authenticated, service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.players TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournament_eligibility_profiles TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournament_eligibility_profile_slots TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournament_registrations TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.tournament_registration_participants TO service_role;

-- The registration as the API reads it: every column plus participants ordered by slot, each with its player summary
-- (the same shape as the PostgREST embed the repository selects). Null when the registration does not exist.
CREATE FUNCTION public.tournament_registration_document(p_registration_id uuid)
RETURNS jsonb LANGUAGE sql STABLE SECURITY INVOKER SET search_path = public, pg_temp AS $$
    SELECT to_jsonb(r) || jsonb_build_object('participants', coalesce((
        SELECT jsonb_agg(jsonb_build_object(
                   'slot_number', p.slot_number,
                   'player_id', p.player_id,
                   'player', jsonb_build_object(
                       'id', pl.id, 'first_name', pl.first_name, 'last_name', pl.last_name, 'club', pl.club,
                       'gender_id', pl.gender_id, 'category_id', pl.category_id, 'birth_date', pl.birth_date))
                   ORDER BY p.slot_number)
          FROM public.tournament_registration_participants p
          JOIN public.players pl ON pl.id = p.player_id
         WHERE p.registration_id = r.id), '[]'::jsonb))
      FROM public.tournament_registrations r
     WHERE r.id = p_registration_id
$$;

-- Creates a registration with its participants at revision 0. p_requested_status 'pending' becomes 'waitlisted' when
-- confirmed plus pending already reach max_pairs; 'confirmed' (backoffice) is refused with capacity_reached when
-- confirmed reach max_pairs. p_participants is [{ "slot_number": 1, "player_id": "..." }, ...] with distinct slots
-- (1..16) and players. The organization is the tournament's. Returns tournament_registration_document.
CREATE FUNCTION public.create_tournament_registration(
    p_tournament_id uuid,
    p_source text,
    p_requested_status text,
    p_payment_status text,
    p_contact_email text,
    p_notes text,
    p_public_name_consent boolean,
    p_participants jsonb,
    p_now_utc timestamptz)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
    v_organization_id uuid;
    v_max_pairs integer;
    v_confirmed integer;
    v_pending integer;
    v_status text;
    v_registration_id uuid;
BEGIN
    IF p_tournament_id IS NULL OR p_now_utc IS NULL OR p_public_name_consent IS NULL
       OR p_requested_status IS NULL OR p_requested_status NOT IN ('pending', 'confirmed')
       OR p_participants IS NULL OR jsonb_typeof(p_participants) <> 'array' OR jsonb_array_length(p_participants) = 0
       OR EXISTS (
           SELECT 1 FROM jsonb_array_elements(p_participants) e
            WHERE jsonb_typeof(e) <> 'object' OR jsonb_typeof(e -> 'slot_number') <> 'number'
               OR jsonb_typeof(e -> 'player_id') <> 'string')
    THEN
        RAISE EXCEPTION 'invalid registration create arguments';
    END IF;
    IF (SELECT count(DISTINCT x.slot_number) <> count(*) OR count(DISTINCT x.player_id) <> count(*)
          FROM jsonb_to_recordset(p_participants) AS x(slot_number integer, player_id uuid)) THEN
        RAISE EXCEPTION 'invalid registration create arguments';
    END IF;

    SELECT t.organization_id, t.max_pairs INTO v_organization_id, v_max_pairs
      FROM public.tournaments t WHERE t.id = p_tournament_id FOR NO KEY UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'registrations.tournament_not_found' USING ERRCODE = 'PT409';
    END IF;

    IF EXISTS (
        SELECT 1 FROM jsonb_to_recordset(p_participants) AS x(slot_number integer, player_id uuid)
         WHERE NOT EXISTS (
             SELECT 1 FROM public.players pl WHERE pl.id = x.player_id AND pl.organization_id = v_organization_id))
    THEN
        RAISE EXCEPTION 'registrations.player_not_found' USING ERRCODE = 'PT409';
    END IF;
    IF EXISTS (
        SELECT 1 FROM jsonb_to_recordset(p_participants) AS x(slot_number integer, player_id uuid)
          JOIN public.tournament_registration_participants p
            ON p.tournament_id = p_tournament_id AND p.player_id = x.player_id AND p.registration_active)
    THEN
        RAISE EXCEPTION 'registrations.player_already_registered' USING ERRCODE = 'PT409';
    END IF;

    SELECT count(*) FILTER (WHERE r.status = 'confirmed'), count(*) FILTER (WHERE r.status = 'pending')
      INTO v_confirmed, v_pending
      FROM public.tournament_registrations r WHERE r.tournament_id = p_tournament_id;

    IF p_requested_status = 'confirmed' THEN
        IF coalesce(v_max_pairs, 0) > 0 AND v_confirmed >= v_max_pairs THEN
            RAISE EXCEPTION 'registrations.capacity_reached' USING ERRCODE = 'PT409';
        END IF;
        v_status := 'confirmed';
    ELSIF coalesce(v_max_pairs, 0) > 0 AND v_confirmed + v_pending >= v_max_pairs THEN
        v_status := 'waitlisted';
    ELSE
        v_status := 'pending';
    END IF;

    INSERT INTO public.tournament_registrations (
        tournament_id, organization_id, status, source, payment_status, contact_email, notes, public_name_consent,
        revision, created_at_utc, updated_at_utc)
    VALUES (
        p_tournament_id, v_organization_id, v_status, p_source, p_payment_status, p_contact_email, p_notes,
        p_public_name_consent, 0, p_now_utc, p_now_utc)
    RETURNING id INTO v_registration_id;

    BEGIN
        INSERT INTO public.tournament_registration_participants (
            registration_id, tournament_id, registration_active, slot_number, player_id)
        SELECT v_registration_id, p_tournament_id, true, x.slot_number, x.player_id
          FROM jsonb_to_recordset(p_participants) AS x(slot_number integer, player_id uuid);
    EXCEPTION WHEN unique_violation THEN
        -- Unreachable while every writer holds the tournament lock; kept for writes that bypass this function.
        RAISE EXCEPTION 'registrations.player_already_registered' USING ERRCODE = 'PT409';
    END;

    RETURN public.tournament_registration_document(v_registration_id);
END $$;

-- Moves the expected registration to p_status at the next revision when the D2 table allows it; confirming needs a
-- free place (confirmed < max_pairs). The tournament is locked before the registration, as in the create function.
-- Returns tournament_registration_document.
CREATE FUNCTION public.transition_tournament_registration(
    p_registration_id uuid,
    p_expected_revision integer,
    p_status text,
    p_now_utc timestamptz)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
    v_tournament_id uuid;
    v_max_pairs integer;
    v_registration public.tournament_registrations;
BEGIN
    IF p_registration_id IS NULL OR p_expected_revision IS NULL OR p_now_utc IS NULL OR p_status IS NULL
       OR p_status NOT IN ('pending', 'confirmed', 'rejected', 'waitlisted', 'cancelled') THEN
        RAISE EXCEPTION 'invalid registration transition arguments';
    END IF;

    -- A registration never changes tournament, so this unlocked read only selects which tournament to lock.
    SELECT r.tournament_id INTO v_tournament_id FROM public.tournament_registrations r WHERE r.id = p_registration_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'registrations.concurrent_modification' USING ERRCODE = 'PT409';
    END IF;
    SELECT t.max_pairs INTO v_max_pairs FROM public.tournaments t WHERE t.id = v_tournament_id FOR NO KEY UPDATE;

    SELECT * INTO v_registration FROM public.tournament_registrations r WHERE r.id = p_registration_id FOR UPDATE;
    IF NOT FOUND OR v_registration.revision <> p_expected_revision THEN
        RAISE EXCEPTION 'registrations.concurrent_modification' USING ERRCODE = 'PT409';
    END IF;

    IF (v_registration.status, p_status) NOT IN (
        ('pending', 'confirmed'), ('pending', 'rejected'), ('pending', 'waitlisted'), ('pending', 'cancelled'),
        ('waitlisted', 'confirmed'), ('waitlisted', 'cancelled'),
        ('confirmed', 'cancelled'))
    THEN
        RAISE EXCEPTION 'registrations.invalid_transition' USING ERRCODE = 'PT409';
    END IF;
    IF p_status = 'confirmed' AND coalesce(v_max_pairs, 0) > 0 AND (
        SELECT count(*) FROM public.tournament_registrations r
         WHERE r.tournament_id = v_tournament_id AND r.status = 'confirmed') >= v_max_pairs
    THEN
        RAISE EXCEPTION 'registrations.capacity_reached' USING ERRCODE = 'PT409';
    END IF;

    UPDATE public.tournament_registrations SET
        status = p_status,
        updated_at_utc = p_now_utc,
        revision = v_registration.revision + 1
    WHERE id = v_registration.id;
    RETURN public.tournament_registration_document(v_registration.id);
END $$;

-- Sets the payment status of the expected registration at the next revision, in any registration status (a cancelled
-- registration may still be refunded). Returns tournament_registration_document.
CREATE FUNCTION public.set_tournament_registration_payment_status(
    p_registration_id uuid,
    p_expected_revision integer,
    p_payment_status text,
    p_now_utc timestamptz)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
    v_registration public.tournament_registrations;
BEGIN
    IF p_registration_id IS NULL OR p_expected_revision IS NULL OR p_now_utc IS NULL OR p_payment_status IS NULL
       OR p_payment_status NOT IN ('not_required', 'pending', 'paid', 'refunded', 'failed') THEN
        RAISE EXCEPTION 'invalid registration payment arguments';
    END IF;

    SELECT * INTO v_registration FROM public.tournament_registrations r WHERE r.id = p_registration_id FOR UPDATE;
    IF NOT FOUND OR v_registration.revision <> p_expected_revision THEN
        RAISE EXCEPTION 'registrations.concurrent_modification' USING ERRCODE = 'PT409';
    END IF;

    UPDATE public.tournament_registrations SET
        payment_status = p_payment_status,
        updated_at_utc = p_now_utc,
        revision = v_registration.revision + 1
    WHERE id = v_registration.id;
    RETURN public.tournament_registration_document(v_registration.id);
END $$;

-- Inserts the profile, or replaces name, description, activity and every slot of the profile p_profile_id of the same
-- organization, in one transaction. A profile id of another organization is refused (eligibility_profiles.not_found)
-- and left unchanged. p_slots is [{ "slot_number", "gender_id", "category_id", "min_age", "max_age", "label",
-- "required" }, ...]. created_at_utc is kept on replace. Returns the profile row with "slots" ordered by slot number.
CREATE FUNCTION public.save_tournament_eligibility_profile(
    p_profile_id uuid,
    p_organization_id uuid,
    p_name text,
    p_description text,
    p_is_active boolean,
    p_slots jsonb,
    p_now_utc timestamptz)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
    v_profile public.tournament_eligibility_profiles;
BEGIN
    IF p_profile_id IS NULL OR p_organization_id IS NULL OR p_is_active IS NULL OR p_now_utc IS NULL
       OR p_slots IS NULL OR jsonb_typeof(p_slots) <> 'array' THEN
        RAISE EXCEPTION 'invalid eligibility profile arguments';
    END IF;

    INSERT INTO public.tournament_eligibility_profiles AS profiles (
        id, organization_id, name, description, is_active, created_at_utc, updated_at_utc)
    VALUES (p_profile_id, p_organization_id, p_name, p_description, p_is_active, p_now_utc, p_now_utc)
    ON CONFLICT (id) DO UPDATE SET
        name = excluded.name,
        description = excluded.description,
        is_active = excluded.is_active,
        updated_at_utc = excluded.updated_at_utc
    WHERE profiles.organization_id = excluded.organization_id
    RETURNING * INTO v_profile;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'eligibility_profiles.not_found' USING ERRCODE = 'PT409';
    END IF;

    DELETE FROM public.tournament_eligibility_profile_slots WHERE profile_id = v_profile.id;
    INSERT INTO public.tournament_eligibility_profile_slots (
        profile_id, slot_number, gender_id, category_id, min_age, max_age, label, required)
    SELECT v_profile.id, x.slot_number, x.gender_id, x.category_id, x.min_age, x.max_age, x.label, coalesce(x.required, true)
      FROM jsonb_to_recordset(p_slots) AS x(
          slot_number integer, gender_id uuid, category_id uuid, min_age integer, max_age integer, label text,
          required boolean);

    RETURN to_jsonb(v_profile) || jsonb_build_object('slots', coalesce((
        SELECT jsonb_agg(to_jsonb(s) - 'profile_id' ORDER BY s.slot_number)
          FROM public.tournament_eligibility_profile_slots s WHERE s.profile_id = v_profile.id), '[]'::jsonb));
END $$;

REVOKE ALL ON FUNCTION public.tournament_registration_document(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.tournament_registration_document(uuid) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.tournament_registration_document(uuid) TO service_role;

REVOKE ALL ON FUNCTION public.create_tournament_registration(uuid,text,text,text,text,text,boolean,jsonb,timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.create_tournament_registration(uuid,text,text,text,text,text,boolean,jsonb,timestamptz) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.create_tournament_registration(uuid,text,text,text,text,text,boolean,jsonb,timestamptz) TO service_role;

REVOKE ALL ON FUNCTION public.transition_tournament_registration(uuid,integer,text,timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.transition_tournament_registration(uuid,integer,text,timestamptz) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.transition_tournament_registration(uuid,integer,text,timestamptz) TO service_role;

REVOKE ALL ON FUNCTION public.set_tournament_registration_payment_status(uuid,integer,text,timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.set_tournament_registration_payment_status(uuid,integer,text,timestamptz) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.set_tournament_registration_payment_status(uuid,integer,text,timestamptz) TO service_role;

REVOKE ALL ON FUNCTION public.save_tournament_eligibility_profile(uuid,uuid,text,text,boolean,jsonb,timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.save_tournament_eligibility_profile(uuid,uuid,text,text,boolean,jsonb,timestamptz) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.save_tournament_eligibility_profile(uuid,uuid,text,text,boolean,jsonb,timestamptz) TO service_role;
