-- Regression checks for the draw revision functions (D12, migration 20261101100000_tournament_draw_revisions).
-- Synthetic UUIDs and keys only; everything runs in one transaction that is rolled back.

DO $$
DECLARE
 rpc regprocedure;
BEGIN
 FOREACH rpc IN ARRAY ARRAY[
  'public.save_tournament_draw_draft(uuid,uuid,uuid,integer,text,text,text,text,jsonb,jsonb,jsonb,timestamptz)'::regprocedure,
  'public.set_tournament_draw_status(uuid,integer,text,timestamptz)'::regprocedure,
  'public.delete_tournament_draw_draft(uuid,integer)'::regprocedure,
  'public.upsert_tournament_draw_match_result(uuid,integer,text,text,jsonb,text,uuid,text,timestamptz,timestamptz,timestamptz,timestamptz)'::regprocedure
 ] LOOP
  IF NOT has_function_privilege('service_role', rpc, 'EXECUTE')
     OR has_function_privilege('anon', rpc, 'EXECUTE')
     OR has_function_privilege('authenticated', rpc, 'EXECUTE')
  THEN
   RAISE EXCEPTION '% grants are not service-role-only', rpc;
  END IF;
  IF (SELECT prosecdef FROM pg_proc WHERE oid = rpc) THEN
   RAISE EXCEPTION '% must be SECURITY INVOKER', rpc;
  END IF;
 END LOOP;
 IF NOT EXISTS (
  SELECT 1 FROM pg_attribute a JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
  WHERE a.attrelid = 'public.tournament_draws'::regclass AND a.attname = 'revision'
    AND a.atttypid = 'integer'::regtype AND a.attnotnull AND pg_get_expr(d.adbin, d.adrelid) = '0'
 ) THEN
  RAISE EXCEPTION 'tournament_draws.revision must be integer not null default 0';
 END IF;
 RAISE NOTICE 'draw revision column, service-role-only execute grants and SECURITY INVOKER: passed';
END $$;

DO $$
DECLARE
 denied boolean;
 role_name text;
BEGIN
 FOREACH role_name IN ARRAY ARRAY['anon', 'authenticated'] LOOP
  denied := false;
  EXECUTE format('SET LOCAL ROLE %I', role_name);
  BEGIN
   PERFORM public.delete_tournament_draw_draft('a6000000-0000-0000-0000-0000000000ff', 0);
  EXCEPTION WHEN insufficient_privilege THEN
   denied := true;
  END;
  EXECUTE 'RESET ROLE';
  IF NOT denied THEN
   RAISE EXCEPTION '% draw function execution was not denied', role_name;
  END IF;
 END LOOP;
 RAISE NOTICE 'anon/authenticated draw function execution denied: passed';
END $$;

BEGIN;

-- The message of a refused write (SQLSTATE PT409), or 'accepted' when the statement ran. A refused statement rolls back
-- to the block's savepoint, so nothing it did remains.
CREATE FUNCTION pg_temp.refusal(statement text) RETURNS text LANGUAGE plpgsql AS $$
BEGIN
 EXECUTE statement;
 RETURN 'accepted';
EXCEPTION WHEN SQLSTATE 'PT409' THEN
 RETURN SQLERRM;
END $$;

INSERT INTO public.tenants (id, name, key, contact_email, plan_type)
VALUES ('a6000000-0000-0000-0000-000000000001', 'Draw tenant', 'a6-draw-tenant', 'draws@a6.test', 'pro');
INSERT INTO public.organizations (id, tenant_id, display_name, type, created_by_user_id)
VALUES ('a6000000-0000-0000-0000-000000000002', 'a6000000-0000-0000-0000-000000000001', 'Draw org', 'estandar',
        'a6000000-0000-0000-0000-000000000003');
INSERT INTO public.sports (id, name, key, icon, icon_source)
VALUES ('a6000000-0000-0000-0000-000000000004', 'Draw sport', 'a6-draw-sport', 'x', 'unicode');
INSERT INTO public.tournaments (id, organization_id, sport_id, name, start_date, end_date, status)
VALUES ('a6000000-0000-0000-0000-000000000005', 'a6000000-0000-0000-0000-000000000002',
        'a6000000-0000-0000-0000-000000000004', 'Draw open', '2026-11-01', '2026-11-02', 'upcoming');

SET LOCAL ROLE service_role;

DO $$
DECLARE
 t uuid := 'a6000000-0000-0000-0000-000000000005';
 o uuid := 'a6000000-0000-0000-0000-000000000002';
 doc jsonb := '{"version":1}';
 saved jsonb;
 written jsonb;
 draw uuid;
 refused text;
 stored record;

 FUNCTION_SAVE constant text := 'SELECT public.save_tournament_draw_draft(%L, %L, %L, %L, ''singleElimination'', '
  || '''pair'', ''seeded'', %L, ''{"version":1}'', ''{"version":1}'', ''{"version":1}'', ''2026-11-01T12:00:00Z'')';
BEGIN
 -- Insert: no draw expected, revision 0.
 saved := public.save_tournament_draw_draft(t, o, NULL, NULL, 'singleElimination', 'pair', 'seeded', '7',
  doc, doc, doc, '2026-11-01T12:00:00Z');
 draw := (saved->>'id')::uuid;
 IF (saved->>'revision')::int <> 0 OR saved->>'status' <> 'draft' OR saved->>'seed' <> '7'
    OR saved->>'tournament_id' <> t::text THEN
  RAISE EXCEPTION 'insert returned %', saved;
 END IF;

 -- A second insert (the draw was created meanwhile), a wrong revision and a wrong draw id are concurrent changes.
 refused := pg_temp.refusal(format(FUNCTION_SAVE, t, o, NULL, NULL, '8'));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'second insert: %', refused; END IF;
 refused := pg_temp.refusal(format(FUNCTION_SAVE, t, o, draw, 5, '8'));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'stale save: %', refused; END IF;
 refused := pg_temp.refusal(format(FUNCTION_SAVE, t, o, gen_random_uuid(), 0, '8'));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'other draw save: %', refused; END IF;
 SELECT seed, revision INTO stored FROM public.tournament_draws WHERE id = draw;
 IF stored.seed <> '7' OR stored.revision <> 0 THEN RAISE EXCEPTION 'refused saves wrote %', stored; END IF;

 -- Replace at the current revision: same id, next revision.
 saved := public.save_tournament_draw_draft(t, o, draw, 0, 'singleElimination', 'pair', 'seeded', '8',
  doc, doc, doc, '2026-11-01T12:01:00Z');
 IF (saved->>'id')::uuid <> draw OR (saved->>'revision')::int <> 1 OR saved->>'seed' <> '8' THEN
  RAISE EXCEPTION 'replace returned %', saved;
 END IF;

 -- Result upsert: check, upsert and bump in one call; a stale revision writes nothing.
 written := public.upsert_tournament_draw_match_result(draw, 1, 'KO-R1-M1', 'scheduled', NULL, NULL, NULL, 'Court 2',
  '2026-11-02T09:00:00Z', NULL, NULL, '2026-11-01T12:02:00Z');
 IF (written->>'revision')::int <> 2 OR written->'result'->>'court_name' <> 'Court 2'
    OR written->'result'->'sets' <> '[]'::jsonb THEN
  RAISE EXCEPTION 'scheduled upsert returned %', written;
 END IF;
 refused := pg_temp.refusal(format(
  'SELECT public.upsert_tournament_draw_match_result(%L, 1, ''KO-R1-M2'', ''inProgress'', ''[]'', NULL, NULL, NULL, '
  || 'NULL, NULL, NULL, ''2026-11-01T12:03:00Z'')', draw));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'stale result: %', refused; END IF;
 IF (SELECT count(*) FROM public.tournament_draw_match_results WHERE draw_id = draw) <> 1
    OR (SELECT revision FROM public.tournament_draws WHERE id = draw) <> 2 THEN
  RAISE EXCEPTION 'stale result wrote';
 END IF;
 written := public.upsert_tournament_draw_match_result(draw, 2, 'KO-R1-M1', 'completed',
  '[{"home":6,"away":4,"tiebreak":null},{"home":6,"away":4,"tiebreak":null}]', 'home', NULL, 'Court 2',
  '2026-11-02T09:00:00Z', NULL, NULL, '2026-11-01T12:04:00Z');
 IF (written->>'revision')::int <> 3 OR written->'result'->>'status' <> 'completed'
    OR (SELECT count(*) FROM public.tournament_draw_match_results WHERE draw_id = draw) <> 1 THEN
  RAISE EXCEPTION 'result replace returned %', written;
 END IF;

 -- A started match blocks replacing and deleting, even at the current revision.
 refused := pg_temp.refusal(format(FUNCTION_SAVE, t, o, draw, 3, '9'));
 IF refused <> 'draws.results_exist' THEN RAISE EXCEPTION 'save with results: %', refused; END IF;
 refused := pg_temp.refusal(format('SELECT public.delete_tournament_draw_draft(%L, 3)', draw));
 IF refused <> 'draws.results_exist' THEN RAISE EXCEPTION 'delete with results: %', refused; END IF;

 -- Publish: stale refused; publishing again keeps the first publication time.
 refused := pg_temp.refusal(format(
  'SELECT public.set_tournament_draw_status(%L, 2, ''published'', ''2026-11-01T12:05:00Z'')', draw));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'stale publish: %', refused; END IF;
 saved := public.set_tournament_draw_status(draw, 3, 'published', '2026-11-01T12:05:00Z');
 IF (saved->>'revision')::int <> 4 OR saved->>'status' <> 'published'
    OR (saved->>'published_at_utc')::timestamptz <> '2026-11-01T12:05:00Z' THEN
  RAISE EXCEPTION 'publish returned %', saved;
 END IF;
 saved := public.set_tournament_draw_status(draw, 4, 'published', '2026-11-01T12:06:00Z');
 IF (saved->>'revision')::int <> 5 OR (saved->>'published_at_utc')::timestamptz <> '2026-11-01T12:05:00Z' THEN
  RAISE EXCEPTION 'republish returned %', saved;
 END IF;

 -- Clearing the result unblocks the delete only once the draw is a draft again.
 written := public.upsert_tournament_draw_match_result(draw, 5, 'KO-R1-M1', 'scheduled', '[]', NULL, NULL, NULL,
  NULL, NULL, NULL, '2026-11-01T12:07:00Z');
 IF (written->>'revision')::int <> 6 THEN RAISE EXCEPTION 'clear returned %', written; END IF;
 refused := pg_temp.refusal(format('SELECT public.delete_tournament_draw_draft(%L, 6)', draw));
 IF refused <> 'draws.not_draft' THEN RAISE EXCEPTION 'delete published: %', refused; END IF;
 saved := public.set_tournament_draw_status(draw, 6, 'draft', '2026-11-01T12:08:00Z');
 IF (saved->>'revision')::int <> 7 OR saved->>'status' <> 'draft' OR saved->'published_at_utc' <> 'null'::jsonb THEN
  RAISE EXCEPTION 'unpublish returned %', saved;
 END IF;

 -- Delete: stale refused, then the draw goes with its results.
 refused := pg_temp.refusal(format('SELECT public.delete_tournament_draw_draft(%L, 6)', draw));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'stale delete: %', refused; END IF;
 written := public.delete_tournament_draw_draft(draw, 7);
 IF (written->>'id')::uuid <> draw OR (written->>'revision')::int <> 8 THEN
  RAISE EXCEPTION 'delete returned %', written;
 END IF;
 IF EXISTS (SELECT 1 FROM public.tournament_draws WHERE id = draw)
    OR EXISTS (SELECT 1 FROM public.tournament_draw_match_results WHERE draw_id = draw) THEN
  RAISE EXCEPTION 'delete left rows';
 END IF;

 -- Every write of a deleted draw is a concurrent change, and a save expecting it does not recreate it.
 refused := pg_temp.refusal(format(
  'SELECT public.set_tournament_draw_status(%L, 7, ''published'', ''2026-11-01T12:09:00Z'')', draw));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'status of deleted: %', refused; END IF;
 refused := pg_temp.refusal(format(
  'SELECT public.upsert_tournament_draw_match_result(%L, 7, ''KO-R1-M1'', ''scheduled'', ''[]'', NULL, NULL, NULL, '
  || 'NULL, NULL, NULL, ''2026-11-01T12:09:00Z'')', draw));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'result of deleted: %', refused; END IF;
 refused := pg_temp.refusal(format('SELECT public.delete_tournament_draw_draft(%L, 7)', draw));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'delete of deleted: %', refused; END IF;
 refused := pg_temp.refusal(format(FUNCTION_SAVE, t, o, draw, 7, '9'));
 IF refused <> 'draws.concurrent_modification' THEN RAISE EXCEPTION 'save of deleted: %', refused; END IF;
 IF EXISTS (SELECT 1 FROM public.tournament_draws WHERE tournament_id = t) THEN
  RAISE EXCEPTION 'save of deleted recreated the draw';
 END IF;

 -- Invalid arguments are not conflicts.
 BEGIN
  PERFORM public.save_tournament_draw_draft(t, o, gen_random_uuid(), NULL, 'singleElimination', 'pair', 'seeded', '7',
   doc, doc, doc, '2026-11-01T12:10:00Z');
  RAISE EXCEPTION 'half expected version accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid draw save arguments' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.set_tournament_draw_status(gen_random_uuid(), 0, 'archived', '2026-11-01T12:10:00Z');
  RAISE EXCEPTION 'unknown status accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid draw status arguments' THEN RAISE; END IF;
 END;

 RAISE NOTICE 'draw save/status/delete/result revision checks, results_exist, not_draft, stale and deleted draws: passed';
END $$;

RESET ROLE;
ROLLBACK;
