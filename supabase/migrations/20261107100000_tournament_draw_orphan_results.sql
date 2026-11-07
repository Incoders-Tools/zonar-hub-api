-- Orphan-aware "results exist" checks for tournament draws (S3, D2). Replaces the bodies of save_tournament_draw_draft
-- and delete_tournament_draw_draft from 20261101100000_tournament_draw_revisions; signatures, SECURITY INVOKER,
-- search_path and grants are kept (re-stated below). No table or row is changed.
--
-- A result row blocks replacing or deleting a draft only when its status is not 'scheduled' AND its match_id is a match
-- of the draw's stored structure: phases[*].rounds[*].matches[*].id or phases[*].groups[*].rounds[*].matches[*].id.
-- A row whose match no longer exists in the structure (orphan) is ignored, as the API already ignores it on read, so
-- it can no longer block a draft forever. Replacing keeps such rows; deleting removes them with the draw (CASCADE).

CREATE OR REPLACE FUNCTION public.save_tournament_draw_draft(
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
        SELECT 1 FROM public.tournament_draw_match_results r
        WHERE r.draw_id = v_draw.id AND r.status <> 'scheduled'
          AND r.match_id IN (
              SELECT jsonb_path_query(v_draw.structure, '$.phases[*].rounds[*].matches[*].id') #>> '{}'
              UNION ALL
              SELECT jsonb_path_query(v_draw.structure, '$.phases[*].groups[*].rounds[*].matches[*].id') #>> '{}')
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

CREATE OR REPLACE FUNCTION public.delete_tournament_draw_draft(p_draw_id uuid, p_expected_revision integer)
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
        SELECT 1 FROM public.tournament_draw_match_results r
        WHERE r.draw_id = v_draw.id AND r.status <> 'scheduled'
          AND r.match_id IN (
              SELECT jsonb_path_query(v_draw.structure, '$.phases[*].rounds[*].matches[*].id') #>> '{}'
              UNION ALL
              SELECT jsonb_path_query(v_draw.structure, '$.phases[*].groups[*].rounds[*].matches[*].id') #>> '{}')
    ) THEN
        RAISE EXCEPTION 'draws.results_exist' USING ERRCODE = 'PT409';
    END IF;
    IF v_draw.status <> 'draft' THEN
        RAISE EXCEPTION 'draws.not_draft' USING ERRCODE = 'PT409';
    END IF;

    DELETE FROM public.tournament_draws WHERE id = v_draw.id;
    RETURN jsonb_build_object('id', v_draw.id, 'revision', v_draw.revision + 1);
END $$;

REVOKE ALL ON FUNCTION public.save_tournament_draw_draft(uuid,uuid,uuid,integer,text,text,text,text,jsonb,jsonb,jsonb,timestamptz) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.save_tournament_draw_draft(uuid,uuid,uuid,integer,text,text,text,text,jsonb,jsonb,jsonb,timestamptz) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.save_tournament_draw_draft(uuid,uuid,uuid,integer,text,text,text,text,jsonb,jsonb,jsonb,timestamptz) TO service_role;

REVOKE ALL ON FUNCTION public.delete_tournament_draw_draft(uuid,integer) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.delete_tournament_draw_draft(uuid,integer) FROM anon, authenticated;
GRANT EXECUTE ON FUNCTION public.delete_tournament_draw_draft(uuid,integer) TO service_role;
