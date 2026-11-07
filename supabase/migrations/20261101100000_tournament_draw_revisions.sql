-- Optimistic concurrency for tournament draws (D12). Additive only: one column with a default and four new functions;
-- no existing row is rewritten beyond the column default. Plain ADD COLUMN / CREATE FUNCTION (no IF NOT EXISTS, no OR
-- REPLACE) so a reused name fails instead of silently keeping a different shape.
--
-- tournament_draws.revision is incremented by every draw write: save draft, publish/unpublish, delete and result
-- upsert. The API (TournamentDrawRepository) reads a draw with its revision, validates it with the domain, then calls
-- one of the functions below with the revision it read. Each function runs in one transaction: it locks the draw row
-- (FOR UPDATE), checks the expected revision, re-checks the state rules it can see, applies the change and bumps the
-- revision, so two writers of the same draw serialize and the second one fails instead of overwriting the first.
--
-- A refused write raises SQLSTATE PT409 (PostgREST answers HTTP 409) with a stable message the repository maps:
--   draws.concurrent_modification  the draw is not at the expected revision, no longer exists, or was created meanwhile
--   draws.results_exist            a match has a result other than 'scheduled' (save and delete only)
--   draws.not_draft                the draw is published (delete only)
-- Any other failure (invalid arguments, check constraints) keeps its own SQLSTATE.
--
-- The functions follow save_complex_with_courts: SECURITY INVOKER, a fixed search_path, and EXECUTE for service_role
-- only. They do not authorize users: the API applies the tournament tenant rule before calling them. Timestamps come
-- from the API clock (p_now_utc) as on the other draw writes; created_at_utc keeps its column default.

ALTER TABLE public.tournament_draws
    ADD COLUMN revision INTEGER NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_tournament_draws_revision CHECK (revision >= 0);

-- Inserts the tournament's draw at revision 0 when no draw is expected (p_expected_draw_id and p_expected_revision both
-- null), or replaces the request, options and structure of the expected draw as a draft (publication cleared) at the
-- next revision. Results are kept: only 'scheduled' rows can exist when a replacement is allowed. Returns the draw row.
CREATE FUNCTION public.save_tournament_draw_draft(
    p_tournament_id uuid,
    p_organization_id uuid,
    p_expected_draw_id uuid,
    p_expected_revision integer,
    p_format text,
    p_participation_format text,
    p_randomization_mode text,
    p_seed text,
    p_options jsonb,
    p_request jsonb,
    p_structure jsonb,
    p_now_utc timestamptz)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
    v_draw public.tournament_draws;
BEGIN
    IF p_tournament_id IS NULL OR p_now_utc IS NULL
       OR (p_expected_draw_id IS NULL) <> (p_expected_revision IS NULL) THEN
        RAISE EXCEPTION 'invalid draw save arguments';
    END IF;

    SELECT * INTO v_draw FROM public.tournament_draws WHERE tournament_id = p_tournament_id FOR UPDATE;
    IF NOT FOUND THEN
        IF p_expected_draw_id IS NOT NULL THEN
            RAISE EXCEPTION 'draws.concurrent_modification' USING ERRCODE = 'PT409';
        END IF;
        -- A concurrent insert of the same tournament wins the unique index; this one then inserts nothing.
        INSERT INTO public.tournament_draws (
            tournament_id, organization_id, status, format, participation_format, randomization_mode, seed,
            options, request, structure, published_at_utc, updated_at_utc, revision)
        VALUES (
            p_tournament_id, p_organization_id, 'draft', p_format, p_participation_format, p_randomization_mode, p_seed,
            p_options, p_request, p_structure, NULL, p_now_utc, 0)
        ON CONFLICT (tournament_id) DO NOTHING
        RETURNING * INTO v_draw;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'draws.concurrent_modification' USING ERRCODE = 'PT409';
        END IF;
        RETURN to_jsonb(v_draw);
    END IF;

    IF v_draw.id IS DISTINCT FROM p_expected_draw_id OR v_draw.revision IS DISTINCT FROM p_expected_revision THEN
        RAISE EXCEPTION 'draws.concurrent_modification' USING ERRCODE = 'PT409';
    END IF;
    IF EXISTS (
        SELECT 1 FROM public.tournament_draw_match_results
        WHERE draw_id = v_draw.id AND status <> 'scheduled'
    ) THEN
        RAISE EXCEPTION 'draws.results_exist' USING ERRCODE = 'PT409';
    END IF;

    UPDATE public.tournament_draws SET
        organization_id = p_organization_id,
        status = 'draft',
        format = p_format,
        participation_format = p_participation_format,
        randomization_mode = p_randomization_mode,
        seed = p_seed,
        options = p_options,
        request = p_request,
        structure = p_structure,
        published_at_utc = NULL,
        updated_at_utc = p_now_utc,
        revision = v_draw.revision + 1
    WHERE id = v_draw.id
    RETURNING * INTO v_draw;
    RETURN to_jsonb(v_draw);
END $$;

-- Publishes (publication time = p_now_utc, kept when already published) or returns the expected draw to draft
-- (publication cleared) at the next revision; results are kept. Returns the draw row.
CREATE FUNCTION public.set_tournament_draw_status(
    p_draw_id uuid,
    p_expected_revision integer,
    p_status text,
    p_now_utc timestamptz)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
    v_draw public.tournament_draws;
BEGIN
    IF p_draw_id IS NULL OR p_expected_revision IS NULL OR p_now_utc IS NULL
       OR p_status IS NULL OR p_status NOT IN ('draft', 'published') THEN
        RAISE EXCEPTION 'invalid draw status arguments';
    END IF;

    SELECT * INTO v_draw FROM public.tournament_draws WHERE id = p_draw_id FOR UPDATE;
    IF NOT FOUND OR v_draw.revision <> p_expected_revision THEN
        RAISE EXCEPTION 'draws.concurrent_modification' USING ERRCODE = 'PT409';
    END IF;

    UPDATE public.tournament_draws SET
        status = p_status,
        published_at_utc = CASE
            WHEN p_status = 'published' THEN coalesce(v_draw.published_at_utc, p_now_utc)
        END,
        updated_at_utc = p_now_utc,
        revision = v_draw.revision + 1
    WHERE id = v_draw.id
    RETURNING * INTO v_draw;
    RETURN to_jsonb(v_draw);
END $$;

-- Deletes the expected draw while it is a draft whose matches have no result other than 'scheduled'; its results go
-- with it (ON DELETE CASCADE). Returns { id, revision } with the revision the delete reached.
CREATE FUNCTION public.delete_tournament_draw_draft(p_draw_id uuid, p_expected_revision integer)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
    v_draw public.tournament_draws;
BEGIN
    IF p_draw_id IS NULL OR p_expected_revision IS NULL THEN
        RAISE EXCEPTION 'invalid draw delete arguments';
    END IF;

    SELECT * INTO v_draw FROM public.tournament_draws WHERE id = p_draw_id FOR UPDATE;
    IF NOT FOUND OR v_draw.revision <> p_expected_revision THEN
        RAISE EXCEPTION 'draws.concurrent_modification' USING ERRCODE = 'PT409';
    END IF;
    IF EXISTS (
        SELECT 1 FROM public.tournament_draw_match_results
        WHERE draw_id = v_draw.id AND status <> 'scheduled'
    ) THEN
        RAISE EXCEPTION 'draws.results_exist' USING ERRCODE = 'PT409';
    END IF;
    IF v_draw.status <> 'draft' THEN
        RAISE EXCEPTION 'draws.not_draft' USING ERRCODE = 'PT409';
    END IF;

    DELETE FROM public.tournament_draws WHERE id = v_draw.id;
    RETURN jsonb_build_object('id', v_draw.id, 'revision', v_draw.revision + 1);
END $$;

-- Inserts or replaces the result of one match of the expected draw and bumps the draw revision, in one transaction.
-- Score rules (D3/D4) are validated by the API before the call. Returns { revision, result } with the stored row.
CREATE FUNCTION public.upsert_tournament_draw_match_result(
    p_draw_id uuid,
    p_expected_revision integer,
    p_match_id text,
    p_status text,
    p_sets jsonb,
    p_winner_side text,
    p_court_id uuid,
    p_court_name text,
    p_scheduled_at_utc timestamptz,
    p_started_at_utc timestamptz,
    p_finished_at_utc timestamptz,
    p_now_utc timestamptz)
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
    v_draw public.tournament_draws;
    v_result public.tournament_draw_match_results;
BEGIN
    IF p_draw_id IS NULL OR p_expected_revision IS NULL OR p_now_utc IS NULL THEN
        RAISE EXCEPTION 'invalid draw result arguments';
    END IF;

    SELECT * INTO v_draw FROM public.tournament_draws WHERE id = p_draw_id FOR UPDATE;
    IF NOT FOUND OR v_draw.revision <> p_expected_revision THEN
        RAISE EXCEPTION 'draws.concurrent_modification' USING ERRCODE = 'PT409';
    END IF;

    INSERT INTO public.tournament_draw_match_results (
        draw_id, match_id, status, sets, winner_side, court_id, court_name,
        scheduled_at_utc, started_at_utc, finished_at_utc, updated_at_utc)
    VALUES (
        v_draw.id, p_match_id, p_status, coalesce(p_sets, '[]'::jsonb), p_winner_side, p_court_id, p_court_name,
        p_scheduled_at_utc, p_started_at_utc, p_finished_at_utc, p_now_utc)
    ON CONFLICT (draw_id, match_id) DO UPDATE SET
        status = excluded.status,
        sets = excluded.sets,
        winner_side = excluded.winner_side,
        court_id = excluded.court_id,
        court_name = excluded.court_name,
        scheduled_at_utc = excluded.scheduled_at_utc,
        started_at_utc = excluded.started_at_utc,
        finished_at_utc = excluded.finished_at_utc,
        updated_at_utc = excluded.updated_at_utc
    RETURNING * INTO v_result;

    UPDATE public.tournament_draws SET revision = v_draw.revision + 1 WHERE id = v_draw.id;
    RETURN jsonb_build_object('revision', v_draw.revision + 1, 'result', to_jsonb(v_result));
END $$;

REVOKE ALL ON FUNCTION public.save_tournament_draw_draft(uuid,uuid,uuid,integer,text,text,text,text,jsonb,jsonb,jsonb,timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.save_tournament_draw_draft(uuid,uuid,uuid,integer,text,text,text,text,jsonb,jsonb,jsonb,timestamptz) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.save_tournament_draw_draft(uuid,uuid,uuid,integer,text,text,text,text,jsonb,jsonb,jsonb,timestamptz) TO service_role;

REVOKE ALL ON FUNCTION public.set_tournament_draw_status(uuid,integer,text,timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.set_tournament_draw_status(uuid,integer,text,timestamptz) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.set_tournament_draw_status(uuid,integer,text,timestamptz) TO service_role;

REVOKE ALL ON FUNCTION public.delete_tournament_draw_draft(uuid,integer) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.delete_tournament_draw_draft(uuid,integer) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.delete_tournament_draw_draft(uuid,integer) TO service_role;

REVOKE ALL ON FUNCTION public.upsert_tournament_draw_match_result(uuid,integer,text,text,jsonb,text,uuid,text,timestamptz,timestamptz,timestamptz,timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.upsert_tournament_draw_match_result(uuid,integer,text,text,jsonb,text,uuid,text,timestamptz,timestamptz,timestamptz,timestamptz) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.upsert_tournament_draw_match_result(uuid,integer,text,text,jsonb,text,uuid,text,timestamptz,timestamptz,timestamptz,timestamptz) TO service_role;
