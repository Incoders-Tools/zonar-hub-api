-- Regression checks for players, eligibility profiles and tournament registrations (registrations R1: S3, S4, D1, D2;
-- migration 20261110100000_tournament_registrations). Synthetic UUIDs and keys only; the data checks run in one
-- transaction that is rolled back.

DO $$
DECLARE
 rel regclass;
 rpc regprocedure;
 privilege text;
BEGIN
 FOREACH rel IN ARRAY ARRAY[
  'public.players'::regclass, 'public.tournament_eligibility_profiles'::regclass,
  'public.tournament_eligibility_profile_slots'::regclass, 'public.tournament_registrations'::regclass,
  'public.tournament_registration_participants'::regclass
 ] LOOP
  IF NOT (SELECT relrowsecurity FROM pg_class WHERE oid = rel) THEN
   RAISE EXCEPTION '% must have row level security enabled', rel;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_policy WHERE polrelid = rel AND polname = 'Service role full access'
                 AND polroles = ARRAY['service_role'::regrole::oid]) THEN
   RAISE EXCEPTION '% must have the service-role-only policy', rel;
  END IF;
  FOREACH privilege IN ARRAY ARRAY['SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER'] LOOP
   IF has_table_privilege('service_role', rel, privilege) IS DISTINCT FROM (privilege IN ('SELECT', 'INSERT', 'UPDATE', 'DELETE'))
      OR has_table_privilege('anon', rel, privilege)
      OR has_table_privilege('authenticated', rel, privilege) THEN
    RAISE EXCEPTION '% % grants are not plain DML for service_role only', rel, privilege;
   END IF;
  END LOOP;
  IF has_any_column_privilege('anon', rel, 'SELECT') OR has_any_column_privilege('authenticated', rel, 'SELECT') THEN
   RAISE EXCEPTION '% has column grants for anon/authenticated', rel;
  END IF;
 END LOOP;

 FOREACH rpc IN ARRAY ARRAY[
  'public.tournament_registration_document(uuid)'::regprocedure,
  'public.create_tournament_registration(uuid,text,text,text,text,text,boolean,jsonb,timestamptz)'::regprocedure,
  'public.transition_tournament_registration(uuid,integer,text,timestamptz)'::regprocedure,
  'public.set_tournament_registration_payment_status(uuid,integer,text,timestamptz)'::regprocedure,
  'public.save_tournament_eligibility_profile(uuid,uuid,text,text,boolean,jsonb,timestamptz)'::regprocedure
 ] LOOP
  IF NOT has_function_privilege('service_role', rpc, 'EXECUTE')
     OR has_function_privilege('anon', rpc, 'EXECUTE')
     OR has_function_privilege('authenticated', rpc, 'EXECUTE') THEN
   RAISE EXCEPTION '% grants are not service-role-only', rpc;
  END IF;
  IF (SELECT prosecdef FROM pg_proc WHERE oid = rpc) THEN
   RAISE EXCEPTION '% must be SECURITY INVOKER', rpc;
  END IF;
 END LOOP;

 -- The legacy table stays as it was (D1): same columns, still referenced by nothing new.
 IF (SELECT string_agg(attname, ',' ORDER BY attnum) FROM pg_attribute
      WHERE attrelid = 'public.registrations'::regclass AND attnum > 0 AND NOT attisdropped)
    <> 'id,organization_id,tournament_id,player_id,status,created_at_utc,updated_at_utc' THEN
  RAISE EXCEPTION 'legacy registrations table changed';
 END IF;
 IF NOT EXISTS (
  SELECT 1 FROM pg_attribute WHERE attrelid = 'public.tournaments'::regclass AND attname = 'eligibility_profile_id'
    AND atttypid = 'uuid'::regtype AND NOT attnotnull) THEN
  RAISE EXCEPTION 'tournaments.eligibility_profile_id must be a nullable uuid';
 END IF;
 RAISE NOTICE 'registration tables RLS, service-role-only DML and execute grants, SECURITY INVOKER, legacy table untouched: passed';
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
   PERFORM public.transition_tournament_registration('a7000000-0000-0000-0000-0000000000ff', 0, 'confirmed', now());
  EXCEPTION WHEN insufficient_privilege THEN
   denied := true;
  END;
  EXECUTE 'RESET ROLE';
  IF NOT denied THEN
   RAISE EXCEPTION '% registration function execution was not denied', role_name;
  END IF;
  denied := false;
  EXECUTE format('SET LOCAL ROLE %I', role_name);
  BEGIN
   PERFORM 1 FROM public.tournament_registrations;
  EXCEPTION WHEN insufficient_privilege THEN
   denied := true;
  END;
  EXECUTE 'RESET ROLE';
  IF NOT denied THEN
   RAISE EXCEPTION '% registration table read was not denied', role_name;
  END IF;
 END LOOP;
 RAISE NOTICE 'anon/authenticated registration function execution and table reads denied: passed';
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

-- The SQLSTATE of a failed statement, or 'accepted'.
CREATE FUNCTION pg_temp.failure(statement text) RETURNS text LANGUAGE plpgsql AS $$
BEGIN
 EXECUTE statement;
 RETURN 'accepted';
EXCEPTION WHEN OTHERS THEN
 RETURN SQLSTATE;
END $$;

INSERT INTO public.tenants (id, name, key, contact_email, plan_type)
VALUES ('a7000000-0000-0000-0000-000000000001', 'Registration tenant', 'a7-registration-tenant', 'reg@a7.test', 'pro');
INSERT INTO public.organizations (id, tenant_id, display_name, type, created_by_user_id)
VALUES ('a7000000-0000-0000-0000-000000000002', 'a7000000-0000-0000-0000-000000000001', 'Registration org', 'estandar',
        'a7000000-0000-0000-0000-000000000003'),
       ('a7000000-0000-0000-0000-000000000012', 'a7000000-0000-0000-0000-000000000001', 'Other org', 'estandar',
        'a7000000-0000-0000-0000-000000000003');
INSERT INTO public.sports (id, name, key, icon, icon_source)
VALUES ('a7000000-0000-0000-0000-000000000004', 'Registration sport', 'a7-registration-sport', 'x', 'unicode');
INSERT INTO public.tournaments (id, organization_id, sport_id, name, start_date, end_date, status, max_pairs)
VALUES ('a7000000-0000-0000-0000-000000000005', 'a7000000-0000-0000-0000-000000000002',
        'a7000000-0000-0000-0000-000000000004', 'Two pairs', '2026-12-01', '2026-12-02', 'upcoming', 2),
       ('a7000000-0000-0000-0000-000000000006', 'a7000000-0000-0000-0000-000000000002',
        'a7000000-0000-0000-0000-000000000004', 'Unlimited', '2026-12-01', '2026-12-02', 'upcoming', NULL);
INSERT INTO public.players (id, organization_id, first_name, last_name, email, club)
SELECT ('a7000000-0000-0000-0000-0000000001' || lpad(n::text, 2, '0'))::uuid,
       CASE WHEN n = 9 THEN 'a7000000-0000-0000-0000-000000000012'::uuid ELSE 'a7000000-0000-0000-0000-000000000002'::uuid END,
       'First' || n, 'Last' || n, 'player' || n || '@a7.test', 'Club ' || (n % 2)
  FROM generate_series(1, 9) n;

SET LOCAL ROLE service_role;

DO $$
DECLARE
 t uuid := 'a7000000-0000-0000-0000-000000000005';
 unlimited uuid := 'a7000000-0000-0000-0000-000000000006';
 now_utc timestamptz := '2026-11-10T12:00:00Z';
 created jsonb;
 written jsonb;
 r1 uuid;
 r2 uuid;
 r3 uuid;
 refused text;
 pair text := '[{"slot_number":2,"player_id":"a7000000-0000-0000-0000-0000000001%s"},'
           || '{"slot_number":1,"player_id":"a7000000-0000-0000-0000-0000000001%s"}]';
 create_sql constant text := 'SELECT public.create_tournament_registration(%L, ''public'', %L, ''pending'', '
  || '''pair@a7.test'', NULL, false, %L, ''2026-11-10T12:00:00Z'')';
BEGIN
 -- Create: pending at revision 0, organization from the tournament, participants by slot with player summaries.
 created := public.create_tournament_registration(t, 'public', 'pending', 'pending', 'pair@a7.test', 'notes', true,
  format(pair, '02', '01')::jsonb, now_utc);
 r1 := (created->>'id')::uuid;
 IF created->>'status' <> 'pending' OR (created->>'revision')::int <> 0
    OR created->>'organization_id' <> 'a7000000-0000-0000-0000-000000000002'
    OR created->>'source' <> 'public' OR created->>'payment_status' <> 'pending'
    OR (created->>'public_name_consent')::boolean IS NOT TRUE OR (created->>'is_active')::boolean IS NOT TRUE
    OR jsonb_array_length(created->'participants') <> 2
    OR created->'participants'->0->>'slot_number' <> '1'
    OR created->'participants'->0->'player'->>'first_name' <> 'First1'
    OR created->'participants'->1->'player'->>'last_name' <> 'Last2'
    OR (created->>'created_at_utc')::timestamptz <> now_utc THEN
  RAISE EXCEPTION 'create returned %', created;
 END IF;

 -- A player with an active registration in the tournament cannot join another one; nothing is written.
 refused := pg_temp.refusal(format(create_sql, t, 'pending', format(pair, '02', '03')));
 IF refused <> 'registrations.player_already_registered' THEN RAISE EXCEPTION 'duplicate player: %', refused; END IF;
 IF (SELECT count(*) FROM public.tournament_registrations WHERE tournament_id = t) <> 1 THEN
  RAISE EXCEPTION 'refused duplicate wrote a registration';
 END IF;
 -- The same player may register in another tournament.
 written := public.create_tournament_registration(unlimited, 'admin', 'confirmed', 'not_required', NULL, NULL, false,
  format(pair, '02', '01')::jsonb, now_utc);
 IF written->>'status' <> 'confirmed' THEN RAISE EXCEPTION 'other tournament returned %', written; END IF;

 -- Players of another organization and unknown tournaments are refused.
 refused := pg_temp.refusal(format(create_sql, t, 'pending', format(pair, '09', '03')));
 IF refused <> 'registrations.player_not_found' THEN RAISE EXCEPTION 'foreign player: %', refused; END IF;
 refused := pg_temp.refusal(format(create_sql, 'a7000000-0000-0000-0000-0000000000ee', 'pending', format(pair, '04', '03')));
 IF refused <> 'registrations.tournament_not_found' THEN RAISE EXCEPTION 'unknown tournament: %', refused; END IF;

 -- Capacity on create: 0 confirmed + 1 pending < 2 stays pending; 0 + 2 >= 2 is waitlisted.
 r2 := (public.create_tournament_registration(t, 'public', 'pending', 'pending', 'b@a7.test', NULL, false,
  format(pair, '04', '03')::jsonb, now_utc)->>'id')::uuid;
 IF (SELECT status FROM public.tournament_registrations WHERE id = r2) <> 'pending' THEN
  RAISE EXCEPTION 'second pair not pending';
 END IF;
 created := public.create_tournament_registration(t, 'public', 'pending', 'pending', 'c@a7.test', NULL, false,
  format(pair, '06', '05')::jsonb, now_utc);
 r3 := (created->>'id')::uuid;
 IF created->>'status' <> 'waitlisted' THEN RAISE EXCEPTION 'full tournament create returned %', created; END IF;

 -- Confirm: stale revision refused without writing; current revision confirms at the next revision.
 refused := pg_temp.refusal(format(
  'SELECT public.transition_tournament_registration(%L, 5, ''confirmed'', ''2026-11-10T12:01:00Z'')', r1));
 IF refused <> 'registrations.concurrent_modification' THEN RAISE EXCEPTION 'stale confirm: %', refused; END IF;
 IF (SELECT status || ':' || revision FROM public.tournament_registrations WHERE id = r1) <> 'pending:0' THEN
  RAISE EXCEPTION 'stale confirm wrote';
 END IF;
 written := public.transition_tournament_registration(r1, 0, 'confirmed', '2026-11-10T12:01:00Z');
 IF written->>'status' <> 'confirmed' OR (written->>'revision')::int <> 1
    OR (written->>'updated_at_utc')::timestamptz <> '2026-11-10T12:01:00Z' OR jsonb_array_length(written->'participants') <> 2 THEN
  RAISE EXCEPTION 'confirm returned %', written;
 END IF;
 PERFORM public.transition_tournament_registration(r2, 0, 'confirmed', '2026-11-10T12:02:00Z');

 -- Confirming beyond max_pairs is refused, from the waitlist and when the backoffice creates confirmed.
 refused := pg_temp.refusal(format(
  'SELECT public.transition_tournament_registration(%L, 0, ''confirmed'', ''2026-11-10T12:03:00Z'')', r3));
 IF refused <> 'registrations.capacity_reached' THEN RAISE EXCEPTION 'confirm over capacity: %', refused; END IF;
 IF (SELECT status || ':' || revision FROM public.tournament_registrations WHERE id = r3) <> 'waitlisted:0' THEN
  RAISE EXCEPTION 'refused confirm wrote';
 END IF;
 refused := pg_temp.refusal(format(
  'SELECT public.create_tournament_registration(%L, ''admin'', ''confirmed'', ''not_required'', NULL, NULL, false, %L, now())',
  t, format(pair, '08', '07')));
 IF refused <> 'registrations.capacity_reached' THEN RAISE EXCEPTION 'confirmed create over capacity: %', refused; END IF;

 -- Transitions outside the D2 table are refused.
 refused := pg_temp.refusal(format(
  'SELECT public.transition_tournament_registration(%L, 1, ''pending'', now())', r1));
 IF refused <> 'registrations.invalid_transition' THEN RAISE EXCEPTION 'confirmed to pending: %', refused; END IF;
 refused := pg_temp.refusal(format(
  'SELECT public.transition_tournament_registration(%L, 1, ''rejected'', now())', r1));
 IF refused <> 'registrations.invalid_transition' THEN RAISE EXCEPTION 'confirmed to rejected: %', refused; END IF;
 refused := pg_temp.refusal(format(
  'SELECT public.transition_tournament_registration(%L, 0, ''rejected'', now())', r3));
 IF refused <> 'registrations.invalid_transition' THEN RAISE EXCEPTION 'waitlisted to rejected: %', refused; END IF;

 -- Cancelling frees the place and the players (participants follow through the cascading key).
 written := public.transition_tournament_registration(r1, 1, 'cancelled', '2026-11-10T12:04:00Z');
 IF written->>'status' <> 'cancelled' OR (written->>'is_active')::boolean OR (written->>'revision')::int <> 2 THEN
  RAISE EXCEPTION 'cancel returned %', written;
 END IF;
 IF EXISTS (SELECT 1 FROM public.tournament_registration_participants WHERE registration_id = r1 AND registration_active) THEN
  RAISE EXCEPTION 'cancelled participants still active';
 END IF;
 refused := pg_temp.refusal(format(
  'SELECT public.transition_tournament_registration(%L, 2, ''confirmed'', now())', r1));
 IF refused <> 'registrations.invalid_transition' THEN RAISE EXCEPTION 'cancelled is not final: %', refused; END IF;
 written := public.transition_tournament_registration(r3, 0, 'confirmed', '2026-11-10T12:05:00Z');
 IF written->>'status' <> 'confirmed' THEN RAISE EXCEPTION 'waitlist confirm returned %', written; END IF;
 -- Full again (2 confirmed): a freed player registers again and is waitlisted.
 written := public.create_tournament_registration(t, 'public', 'pending', 'pending', 'd@a7.test', NULL, false,
  format(pair, '07', '01')::jsonb, now_utc);
 IF written->>'status' <> 'waitlisted' THEN RAISE EXCEPTION 'freed player returned %', written; END IF;

 -- Payment status: stale refused, current bumps the revision in any status.
 refused := pg_temp.refusal(format(
  'SELECT public.set_tournament_registration_payment_status(%L, 0, ''paid'', now())', r1));
 IF refused <> 'registrations.concurrent_modification' THEN RAISE EXCEPTION 'stale payment: %', refused; END IF;
 written := public.set_tournament_registration_payment_status(r1, 2, 'refunded', '2026-11-10T12:06:00Z');
 IF written->>'payment_status' <> 'refunded' OR (written->>'revision')::int <> 3 OR written->>'status' <> 'cancelled' THEN
  RAISE EXCEPTION 'payment returned %', written;
 END IF;

 -- A deleted registration is a concurrent change for every write.
 DELETE FROM public.tournament_registrations WHERE id = r2;
 IF EXISTS (SELECT 1 FROM public.tournament_registration_participants WHERE registration_id = r2) THEN
  RAISE EXCEPTION 'delete left participants';
 END IF;
 refused := pg_temp.refusal(format('SELECT public.transition_tournament_registration(%L, 1, ''cancelled'', now())', r2));
 IF refused <> 'registrations.concurrent_modification' THEN RAISE EXCEPTION 'transition of deleted: %', refused; END IF;
 refused := pg_temp.refusal(format('SELECT public.set_tournament_registration_payment_status(%L, 1, ''paid'', now())', r2));
 IF refused <> 'registrations.concurrent_modification' THEN RAISE EXCEPTION 'payment of deleted: %', refused; END IF;
 IF public.tournament_registration_document(r2) IS NOT NULL THEN RAISE EXCEPTION 'document of deleted is not null'; END IF;

 -- Invalid arguments are not conflicts.
 BEGIN
  PERFORM public.create_tournament_registration(t, 'public', 'waitlisted', 'pending', NULL, NULL, false,
   format(pair, '08', '07')::jsonb, now_utc);
  RAISE EXCEPTION 'waitlisted request accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid registration create arguments' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.create_tournament_registration(t, 'public', 'pending', 'pending', NULL, NULL, false,
   '[{"slot_number":1,"player_id":"a7000000-0000-0000-0000-000000000107"},{"slot_number":1,"player_id":"a7000000-0000-0000-0000-000000000108"}]',
   now_utc);
  RAISE EXCEPTION 'duplicate slot accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid registration create arguments' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.create_tournament_registration(t, 'public', 'pending', 'pending', NULL, NULL, false, '[]', now_utc);
  RAISE EXCEPTION 'empty participants accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid registration create arguments' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.transition_tournament_registration(r3, 1, 'archived', now_utc);
  RAISE EXCEPTION 'unknown status accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid registration transition arguments' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.set_tournament_registration_payment_status(r3, 1, 'free', now_utc);
  RAISE EXCEPTION 'unknown payment status accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid registration payment arguments' THEN RAISE; END IF;
 END;
 IF pg_temp.failure(format(create_sql, t, 'pending', format(pair, '08', '02'))) <> 'accepted' THEN
  RAISE EXCEPTION 'valid create failed';
 END IF;
 IF pg_temp.failure(replace(format(create_sql, unlimited, 'pending', format(pair, '06', '05')), '''public''', '''kiosk''')) <> '23514' THEN
  RAISE EXCEPTION 'unknown source was not a check violation';
 END IF;

 RAISE NOTICE 'registration create/transition/payment: duplicate player, foreign player, capacity, waitlist, invalid transition, stale and deleted: passed';
END $$;

DO $$
DECLARE
 t uuid := 'a7000000-0000-0000-0000-000000000006';
 active_registration uuid;
 other_registration uuid;
BEGIN
 -- The one-active-registration rule holds for direct table writes too, and the copied key cannot drift.
 SELECT registration_id INTO active_registration FROM public.tournament_registration_participants
  WHERE tournament_id = t AND player_id = 'a7000000-0000-0000-0000-000000000101' AND registration_active;
 INSERT INTO public.tournament_registrations (id, tournament_id, organization_id, source, payment_status)
 VALUES ('a7000000-0000-0000-0000-0000000000a1', t, 'a7000000-0000-0000-0000-000000000002', 'admin', 'not_required')
 RETURNING id INTO other_registration;
 IF pg_temp.failure(format(
   'INSERT INTO public.tournament_registration_participants VALUES (%L, %L, true, 1, ''a7000000-0000-0000-0000-000000000101'')',
   other_registration, t)) <> '23505' THEN
  RAISE EXCEPTION 'direct duplicate active participant was not a unique violation';
 END IF;
 IF pg_temp.failure(format(
   'INSERT INTO public.tournament_registration_participants VALUES (%L, %L, false, 1, ''a7000000-0000-0000-0000-000000000101'')',
   other_registration, t)) <> '23503' THEN
  RAISE EXCEPTION 'participant with a stale active flag was not a foreign key violation';
 END IF;
 IF pg_temp.failure(format(
   'UPDATE public.tournament_registration_participants SET registration_active = false WHERE registration_id = %L',
   active_registration)) <> '23503' THEN
  RAISE EXCEPTION 'participant active flag drift was not a foreign key violation';
 END IF;
 IF pg_temp.failure(format(
   'INSERT INTO public.tournament_registration_participants VALUES (%L, ''a7000000-0000-0000-0000-000000000005'', true, 1, ''a7000000-0000-0000-0000-000000000104'')',
   other_registration)) <> '23503' THEN
  RAISE EXCEPTION 'participant of another tournament was not a foreign key violation';
 END IF;
 IF pg_temp.failure('DELETE FROM public.players WHERE id = ''a7000000-0000-0000-0000-000000000101''') <> '23503' THEN
  RAISE EXCEPTION 'registered player delete was not restricted';
 END IF;

 -- Players: unique email (case-insensitive) and document per organization, lowercase email only.
 IF pg_temp.failure('INSERT INTO public.players (organization_id, first_name, last_name, email) VALUES '
   || '(''a7000000-0000-0000-0000-000000000002'', ''A'', ''B'', ''player1@a7.test'')') <> '23505' THEN
  RAISE EXCEPTION 'duplicate player email accepted';
 END IF;
 IF pg_temp.failure('INSERT INTO public.players (organization_id, first_name, last_name, email) VALUES '
   || '(''a7000000-0000-0000-0000-000000000012'', ''A'', ''B'', ''player1@a7.test'')') <> 'accepted' THEN
  RAISE EXCEPTION 'same email in another organization refused';
 END IF;
 IF pg_temp.failure('INSERT INTO public.players (organization_id, first_name, last_name, email) VALUES '
   || '(''a7000000-0000-0000-0000-000000000002'', ''A'', ''B'', ''Upper@a7.test'')') <> '23514' THEN
  RAISE EXCEPTION 'non-lowercase email accepted';
 END IF;
 INSERT INTO public.players (organization_id, first_name, last_name, document_id)
 VALUES ('a7000000-0000-0000-0000-000000000002', 'D', 'One', '30111222');
 IF pg_temp.failure('INSERT INTO public.players (organization_id, first_name, last_name, document_id) VALUES '
   || '(''a7000000-0000-0000-0000-000000000002'', ''D'', ''Two'', ''30111222'')') <> '23505' THEN
  RAISE EXCEPTION 'duplicate player document accepted';
 END IF;

 RAISE NOTICE 'declarative one-active-registration rule, cascading participant key, player uniqueness: passed';
END $$;

DO $$
DECLARE
 o uuid := 'a7000000-0000-0000-0000-000000000002';
 p uuid := 'a7000000-0000-0000-0000-0000000000c1';
 saved jsonb;
 refused text;
BEGIN
 saved := public.save_tournament_eligibility_profile(p, o, 'Damas 4ta', NULL, true,
  '[{"slot_number":2,"gender_id":"a1000000-0000-0000-0000-000000000002","min_age":18},{"slot_number":1,"label":"Player 1","required":false}]',
  '2026-11-10T12:00:00Z');
 IF saved->>'id' <> p::text OR jsonb_array_length(saved->'slots') <> 2 OR saved->'slots'->0->>'slot_number' <> '1'
    OR (saved->'slots'->0->>'required')::boolean OR NOT (saved->'slots'->1->>'required')::boolean
    OR saved->'slots'->1->>'min_age' <> '18' OR saved->'slots'->0 ? 'profile_id' THEN
  RAISE EXCEPTION 'profile insert returned %', saved;
 END IF;

 -- Replace: same id and creation time, slots replaced.
 saved := public.save_tournament_eligibility_profile(p, o, 'Damas 4ta+', 'desc', false,
  '[{"slot_number":1}]', '2026-11-10T13:00:00Z');
 IF saved->>'name' <> 'Damas 4ta+' OR (saved->>'is_active')::boolean OR jsonb_array_length(saved->'slots') <> 1
    OR (saved->>'created_at_utc')::timestamptz <> '2026-11-10T12:00:00Z'
    OR (saved->>'updated_at_utc')::timestamptz <> '2026-11-10T13:00:00Z' THEN
  RAISE EXCEPTION 'profile replace returned %', saved;
 END IF;

 -- Another organization cannot take over the profile.
 refused := pg_temp.refusal(format(
  'SELECT public.save_tournament_eligibility_profile(%L, ''a7000000-0000-0000-0000-000000000012'', ''Stolen'', NULL, true, ''[]'', now())', p));
 IF refused <> 'eligibility_profiles.not_found' THEN RAISE EXCEPTION 'foreign profile save: %', refused; END IF;
 IF (SELECT name FROM public.tournament_eligibility_profiles WHERE id = p) <> 'Damas 4ta+' THEN
  RAISE EXCEPTION 'foreign save changed the profile';
 END IF;

 -- Unique name per organization (case-insensitive); invalid slots keep their check SQLSTATE.
 IF pg_temp.failure(format('SELECT public.save_tournament_eligibility_profile(%L, %L, ''DAMAS 4TA+'', NULL, true, ''[]'', now())',
   gen_random_uuid(), o)) <> '23505' THEN
  RAISE EXCEPTION 'duplicate profile name accepted';
 END IF;
 IF pg_temp.failure(format('SELECT public.save_tournament_eligibility_profile(%L, %L, ''Ages'', NULL, true, '
   || '''[{"slot_number":1,"min_age":40,"max_age":30}]'', now())', gen_random_uuid(), o)) <> '23514' THEN
  RAISE EXCEPTION 'inverted age range accepted';
 END IF;

 -- A profile used by a tournament cannot be deleted.
 UPDATE public.tournaments SET eligibility_profile_id = p WHERE id = 'a7000000-0000-0000-0000-000000000005';
 IF pg_temp.failure(format('DELETE FROM public.tournament_eligibility_profiles WHERE id = %L', p)) <> '23503' THEN
  RAISE EXCEPTION 'referenced profile delete was not restricted';
 END IF;
 UPDATE public.tournaments SET eligibility_profile_id = NULL WHERE id = 'a7000000-0000-0000-0000-000000000005';
 DELETE FROM public.tournament_eligibility_profiles WHERE id = p;
 IF EXISTS (SELECT 1 FROM public.tournament_eligibility_profile_slots WHERE profile_id = p) THEN
  RAISE EXCEPTION 'profile delete left slots';
 END IF;

 RAISE NOTICE 'eligibility profile save/replace, organization ownership, unique name, age check, restricted delete: passed';
END $$;

ROLLBACK;
