DO $$
DECLARE rpc regprocedure := 'public.save_complex_with_courts(uuid,uuid,jsonb,jsonb,uuid[])'::regprocedure;
BEGIN
 IF NOT has_function_privilege('service_role', rpc, 'EXECUTE')
    OR has_function_privilege('anon', rpc, 'EXECUTE')
    OR has_function_privilege('authenticated', rpc, 'EXECUTE')
 THEN
  RAISE EXCEPTION 'RPC grants are not service-role-only';
 END IF;
 RAISE NOTICE 'service-role-only RPC execute grants: passed';
END $$;

-- Exercise actual execution denial as well as catalog privilege checks.
DO $$
DECLARE
 denied boolean;
 role_name text;
BEGIN
 FOREACH role_name IN ARRAY ARRAY['anon', 'authenticated'] LOOP
  denied := false;
  EXECUTE format('SET LOCAL ROLE %I', role_name);
  BEGIN
   PERFORM public.save_complex_with_courts(NULL, NULL, '{}'::jsonb, '[]'::jsonb, '{}'::uuid[]);
  EXCEPTION WHEN insufficient_privilege THEN
   denied := true;
  END;
  EXECUTE 'RESET ROLE';
  IF NOT denied THEN
   RAISE EXCEPTION '% execution was not denied', role_name;
  END IF;
 END LOOP;
END $$;

INSERT INTO public.organizations (id, tenant_id, display_name, type, created_by_user_id)
VALUES
 ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'aaaaaaaa-0000-0000-0000-000000000000', 'Alpha', 'estandar', 'aaaaaaaa-0000-0000-0000-000000000001'),
 ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'bbbbbbbb-0000-0000-0000-000000000000', 'Beta', 'estandar', 'bbbbbbbb-0000-0000-0000-000000000001');
INSERT INTO public.sports (id, name, key, icon, icon_source)
VALUES
 ('cccccccc-cccc-cccc-cccc-cccccccccccc', 'Fixture sport', 'fixture-sport', 'F', 'unicode'),
 ('dddddddd-dddd-dddd-dddd-dddddddddddd', 'Replacement sport', 'replacement-sport', 'R', 'unicode');

-- Fixtures are inserted as postgres; all successful RPC calls below run as service_role.
SET ROLE service_role;
DO $$
DECLARE result jsonb;
BEGIN
 result := public.save_complex_with_courts('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Original","address":"Street"}', '[{"id":"22222222-2222-2222-2222-222222222222","name":"First","sport_ids":["cccccccc-cccc-cccc-cccc-cccccccccccc"]},{"id":"33333333-3333-3333-3333-333333333333","name":"Second","is_indoor":true,"sport_ids":["cccccccc-cccc-cccc-cccc-cccccccccccc"]}]', '{}');
 IF (result->>'complex_id')::uuid <> '11111111-1111-1111-1111-111111111111'
    OR (result->>'court_count')::int <> 2
    OR result->'court_ids' <> '["22222222-2222-2222-2222-222222222222", "33333333-3333-3333-3333-333333333333"]'::jsonb
 THEN
  RAISE EXCEPTION 'initial return payload: %', result;
 END IF;
 IF NOT EXISTS (
  SELECT 1 FROM public.court_sports
  WHERE court_id = '33333333-3333-3333-3333-333333333333'
    AND sport_id = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
 ) THEN
  RAISE EXCEPTION 'initial sport association missing';
 END IF;
 BEGIN
 PERFORM public.save_complex_with_courts('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Changed","address":"Street"}', '[{"id":"44444444-4444-4444-4444-444444444444","name":"Third"},{"id":"55555555-5555-5555-5555-555555555555","name":"Invalid","sport_ids":["66666666-6666-6666-6666-666666666666"]}]', '{}');
 RAISE EXCEPTION 'invalid sport accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL;
 END;
 IF (SELECT name FROM public.complexes WHERE id = '11111111-1111-1111-1111-111111111111') <> 'Original'
    OR EXISTS (
     SELECT 1 FROM public.courts
     WHERE id = '44444444-4444-4444-4444-444444444444'
    ) THEN
  RAISE EXCEPTION 'rollback failed';
 END IF;
 result := public.save_complex_with_courts('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Original","address":"Street"}', '[]', ARRAY['22222222-2222-2222-2222-222222222222']::uuid[]);
 IF (result->>'court_count')::int <> 1
    OR result->'court_ids' <> '[]'::jsonb
    OR EXISTS (SELECT 1 FROM public.court_sports WHERE court_id = '22222222-2222-2222-2222-222222222222')
    OR NOT EXISTS (SELECT 1 FROM public.court_sports WHERE court_id = '33333333-3333-3333-3333-333333333333' AND sport_id = 'cccccccc-cccc-cccc-cccc-cccccccccccc')
    OR EXISTS (SELECT 1 FROM public.courts WHERE id='22222222-2222-2222-2222-222222222222')
    OR NOT EXISTS (SELECT 1 FROM public.courts WHERE id='33333333-3333-3333-3333-333333333333' AND is_indoor)
 THEN
  RAISE EXCEPTION 'explicit deletion or omitted retention failed: %', result;
 END IF;
 result := public.save_complex_with_courts(
  '11111111-1111-1111-1111-111111111111',
  'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  '{"name":"Original","address":"Street"}',
  '[{"id":"33333333-3333-3333-3333-333333333333","name":"Second","sport_ids":["dddddddd-dddd-dddd-dddd-dddddddddddd"]}]',
  '{}'
 );
 IF NOT EXISTS (SELECT 1 FROM public.court_sports WHERE court_id = '33333333-3333-3333-3333-333333333333' AND sport_id = 'dddddddd-dddd-dddd-dddd-dddddddddddd')
    OR EXISTS (SELECT 1 FROM public.court_sports WHERE court_id = '33333333-3333-3333-3333-333333333333' AND sport_id = 'cccccccc-cccc-cccc-cccc-cccccccccccc')
 THEN
  RAISE EXCEPTION 'edited court sport replacement failed';
 END IF;
 result := public.save_complex_with_courts(NULL, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Generated","address":"Street"}', '[{"name":"Generated court","sport_ids":["cccccccc-cccc-cccc-cccc-cccccccccccc"]},{"id":null,"name":"Another court"}]', '{}');
 IF (result->>'complex_id')::uuid IS NULL
    OR (result->>'court_count')::int <> 2
    OR jsonb_array_length(result->'court_ids') <> 2
    OR (result->'court_ids'->>0)::uuid IS NULL
    OR (result->'court_ids'->>1)::uuid IS NULL
    OR (result->'court_ids'->>0) = (result->'court_ids'->>1)
    OR NOT EXISTS (SELECT 1 FROM public.court_sports WHERE court_id=(result->'court_ids'->>0)::uuid AND sport_id='cccccccc-cccc-cccc-cccc-cccccccccccc')
 THEN
  RAISE EXCEPTION 'generated identifiers or sport association missing: %', result;
 END IF;
 IF EXISTS (
  SELECT 1 FROM public.courts
  WHERE complex_id = (result->>'complex_id')::uuid
    AND id <> ALL(ARRAY[(result->'court_ids'->>0)::uuid, (result->'court_ids'->>1)::uuid])
 ) THEN
  RAISE EXCEPTION 'unexpected generated court';
 END IF;
 BEGIN
  PERFORM public.save_complex_with_courts('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Changed","address":"Street"}', '[{"id":"33333333-3333-3333-3333-333333333333","name":"Updated","sport_ids":[]},{"name":"New","sport_ids":["66666666-6666-6666-6666-666666666666"]}]', '{}');
  RAISE EXCEPTION 'invalid association accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL;
 END;
 IF (SELECT name FROM public.courts WHERE id='33333333-3333-3333-3333-333333333333') <> 'Second'
    OR NOT EXISTS (SELECT 1 FROM public.complexes WHERE id='11111111-1111-1111-1111-111111111111' AND name='Original')
 THEN
  RAISE EXCEPTION 'association rollback failed';
 END IF;
 BEGIN
  PERFORM public.save_complex_with_courts('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Changed","address":"Street"}', format('[{"id":"%s","name":"Foreign"}]', result->'court_ids'->>0)::jsonb, '{}');
  RAISE EXCEPTION 'foreign court accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM = 'foreign court accepted' THEN RAISE; END IF;
 END;
 IF (SELECT name FROM public.complexes WHERE id = '11111111-1111-1111-1111-111111111111') <> 'Original' THEN
  RAISE EXCEPTION 'foreign court rollback failed';
 END IF;
 BEGIN
  PERFORM public.save_complex_with_courts('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Original","address":"Street"}', '[{"id":"33333333-3333-3333-3333-333333333333","name":"One"},{"id":"33333333-3333-3333-3333-333333333333","name":"Two"}]', '{}');
  RAISE EXCEPTION 'duplicate accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM = 'duplicate accepted' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.save_complex_with_courts('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Original","address":"Street"}', '[{"id":"33333333-3333-3333-3333-333333333333","name":"One"}]', ARRAY['33333333-3333-3333-3333-333333333333']::uuid[]);
  RAISE EXCEPTION 'overlap accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM = 'overlap accepted' THEN RAISE; END IF;
 END;
 RAISE NOTICE 'rollback, retention, deletion, generated IDs, foreign/duplicate/overlap: passed';
END $$;
RESET ROLE;

-- Ownership conflicts must reject the request without modifying either owner.
DO $$
BEGIN
 INSERT INTO public.complexes (id, organization_id, name, address)
 VALUES ('77777777-7777-7777-7777-777777777777', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'Beta complex', 'Street');
 INSERT INTO public.courts (id, complex_id, name)
 VALUES ('88888888-8888-8888-8888-888888888888', '77777777-7777-7777-7777-777777777777', 'Beta court');
END $$;
SET ROLE service_role;
DO $$
BEGIN
 BEGIN
  PERFORM public.save_complex_with_courts('77777777-7777-7777-7777-777777777777', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Stolen","address":"Street"}', '[]', '{}');
  RAISE EXCEPTION 'foreign complex accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'complex belongs to another organization' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.save_complex_with_courts('11111111-1111-1111-1111-111111111111', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '{"name":"Original","address":"Street"}', '[{"id":"88888888-8888-8888-8888-888888888888","name":"Stolen"}]', '{}');
  RAISE EXCEPTION 'foreign court accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'court belongs to another complex' THEN RAISE; END IF;
 END;
 IF NOT EXISTS (SELECT 1 FROM public.complexes WHERE id='77777777-7777-7777-7777-777777777777' AND organization_id='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' AND name='Beta complex')
    OR NOT EXISTS (SELECT 1 FROM public.courts WHERE id='88888888-8888-8888-8888-888888888888' AND complex_id='77777777-7777-7777-7777-777777777777' AND name='Beta court') THEN
  RAISE EXCEPTION 'foreign owner changed';
 END IF;
 RAISE NOTICE 'foreign ownership rejection and owner preservation: passed';
END $$;
RESET ROLE;
