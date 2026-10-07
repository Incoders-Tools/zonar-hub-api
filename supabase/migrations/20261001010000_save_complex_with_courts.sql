ALTER TABLE public.courts
 ADD COLUMN IF NOT EXISTS surface_type text NULL,
 ADD COLUMN IF NOT EXISTS is_indoor boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS public.court_sports (
 court_id uuid NOT NULL REFERENCES public.courts(id) ON DELETE CASCADE,
 sport_id uuid NOT NULL REFERENCES public.sports(id) ON DELETE RESTRICT,
 PRIMARY KEY (court_id, sport_id)
);
CREATE INDEX IF NOT EXISTS court_sports_sport_id_idx ON public.court_sports(sport_id);
ALTER TABLE public.court_sports ENABLE ROW LEVEL SECURITY;
DO $$
BEGIN
 IF NOT EXISTS (
  SELECT 1 FROM pg_policy
  WHERE polrelid = 'public.court_sports'::regclass AND polname = 'Service role full access'
 ) THEN
  CREATE POLICY "Service role full access" ON public.court_sports TO service_role USING (true) WITH CHECK (true);
 END IF;
END $$;
-- Hosted default privileges grant new public tables to every API role, and RLS
-- does not govern TRUNCATE; drop them and keep only the RPC's DML for service_role.
REVOKE ALL ON TABLE public.court_sports FROM PUBLIC, anon, authenticated, service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.court_sports TO service_role;

-- IF NOT EXISTS skips objects that already exist, so fail closed when a reused
-- name differs from this migration, including extra constraints, indexes or policies.
DO $$
DECLARE
 previous_path text := current_setting('search_path');
 drift text;
BEGIN
 -- Render catalog definitions fully qualified for this comparison only.
 PERFORM set_config('search_path', 'pg_catalog', true);
 WITH expected(item) AS (VALUES
  ('public.courts columns: surface_type text, is_indoor boolean not null default false'),
  ('public.court_sports columns: court_id uuid not null, sport_id uuid not null'),
  ('public.court_sports PRIMARY KEY (court_id, sport_id)'),
  ('public.court_sports FOREIGN KEY (court_id) REFERENCES public.courts(id) ON DELETE CASCADE'),
  ('public.court_sports FOREIGN KEY (sport_id) REFERENCES public.sports(id) ON DELETE RESTRICT'),
  ('CREATE INDEX court_sports_sport_id_idx ON public.court_sports USING btree (sport_id)'),
  ('public.court_sports rls true'),
  ('public.court_sports policy "Service role full access" permissive * {service_role} using true check true'),
  ('public.court_sports acl service_role DELETE, service_role INSERT, service_role SELECT, service_role UPDATE')
 ), actual(item) AS (
  SELECT format('%s columns: %s', a.attrelid::regclass, string_agg(
          format('%s %s', a.attname, format_type(a.atttypid, a.atttypmod))
          || CASE WHEN a.attnotnull THEN ' not null' ELSE '' END
          || coalesce(' default ' || pg_get_expr(d.adbin, d.adrelid), ''),
          ', ' ORDER BY a.attnum))
  FROM pg_attribute a
  LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
  WHERE a.attnum > 0 AND NOT a.attisdropped
    AND (a.attrelid = 'public.court_sports'::regclass
         OR (a.attrelid = 'public.courts'::regclass AND a.attname IN ('surface_type', 'is_indoor')))
  GROUP BY a.attrelid
  UNION ALL
  SELECT format('%s %s', c.conrelid::regclass, pg_get_constraintdef(c.oid))
  FROM pg_constraint c WHERE c.conrelid = 'public.court_sports'::regclass
  UNION ALL
  SELECT pg_get_indexdef(i.indexrelid)
  FROM pg_index i WHERE i.indrelid = 'public.court_sports'::regclass AND NOT i.indisprimary
  UNION ALL
  SELECT format('%s rls %s', t.oid::regclass, t.relrowsecurity::text)
  FROM pg_class t WHERE t.oid = 'public.court_sports'::regclass
  UNION ALL
  SELECT format('%s policy %I %s %s %s using %s check %s', p.polrelid::regclass, p.polname,
          CASE WHEN p.polpermissive THEN 'permissive' ELSE 'restrictive' END, p.polcmd,
          p.polroles::regrole[], pg_get_expr(p.polqual, p.polrelid),
          pg_get_expr(p.polwithcheck, p.polrelid))
  FROM pg_policy p WHERE p.polrelid = 'public.court_sports'::regclass
  UNION ALL
  -- Non-owner table privileges; the owner keeps its implicit maintenance rights.
  SELECT format('%s acl %s', t.oid::regclass, coalesce((
          SELECT string_agg(g.item, ', ' ORDER BY g.item)
          FROM (SELECT format('%s %s%s',
                 CASE WHEN e.grantee = 0 THEN 'PUBLIC' ELSE e.grantee::regrole::text END,
                 e.privilege_type, CASE WHEN e.is_grantable THEN ' grantable' ELSE '' END)
                FROM aclexplode(coalesce(t.relacl, acldefault('r', t.relowner))) e
                WHERE e.grantee <> t.relowner) g(item)), 'none'))
  FROM pg_class t WHERE t.oid = 'public.court_sports'::regclass
  UNION ALL
  SELECT format('%s column %s acl %s', a.attrelid::regclass, a.attname, a.attacl)
  FROM pg_attribute a
  WHERE a.attrelid = 'public.court_sports'::regclass AND cardinality(a.attacl) > 0
 )
 SELECT string_agg(item, '; ') INTO drift
 FROM ((SELECT item FROM expected EXCEPT ALL SELECT item FROM actual)
       UNION ALL
       (SELECT item FROM actual EXCEPT ALL SELECT item FROM expected)) mismatch;
 PERFORM set_config('search_path', previous_path, true);
 IF drift IS NOT NULL THEN
  RAISE EXCEPTION 'court schema drift: %', drift;
 END IF;
END $$;

-- Replacing keeps the exact signature, SECURITY INVOKER and search_path; a changed
-- return type or parameter name fails, and the grants below are reasserted.
CREATE OR REPLACE FUNCTION public.save_complex_with_courts(p_complex_id uuid, p_organization_id uuid, p_complex jsonb, p_courts jsonb, p_delete_court_ids uuid[])
RETURNS jsonb LANGUAGE plpgsql SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
 court jsonb;
 sport jsonb;
 court_id uuid;
 sport_id uuid;
 deleted_id uuid;
 seen_courts uuid[] := '{}';
 returned_courts uuid[] := '{}';
 seen_sports uuid[];
 count_courts integer;
BEGIN
 -- Only the service role may execute this RPC. The API must validate caller identity
 -- and organization scope before invoking it; this function does not authorize users.
 IF p_organization_id IS NULL OR NOT EXISTS (
  SELECT 1 FROM public.organizations WHERE id = p_organization_id
 ) THEN
  RAISE EXCEPTION 'invalid organization id';
 END IF;
 p_complex_id := coalesce(p_complex_id, gen_random_uuid());
 IF jsonb_typeof(p_complex) <> 'object'
    OR jsonb_typeof(p_courts) <> 'array'
    OR p_delete_court_ids IS NULL THEN
  RAISE EXCEPTION 'invalid aggregate payload';
 END IF;
 IF nullif(btrim(p_complex->>'name'), '') IS NULL
    OR nullif(btrim(p_complex->>'address'), '') IS NULL THEN
  RAISE EXCEPTION 'complex name and address required';
 END IF;
 IF EXISTS (
  SELECT 1 FROM public.complexes
  WHERE id = p_complex_id AND organization_id <> p_organization_id
 ) THEN
  RAISE EXCEPTION 'complex belongs to another organization';
 END IF;
 IF EXISTS (SELECT 1 FROM unnest(p_delete_court_ids) id WHERE id IS NULL)
    OR (SELECT count(*) FROM unnest(p_delete_court_ids)) <>
       (SELECT count(DISTINCT id) FROM unnest(p_delete_court_ids) id) THEN
  RAISE EXCEPTION 'duplicate or null delete id';
 END IF;
 INSERT INTO public.complexes (
  id, organization_id, name, address, location, is_active, key,
  sort_order, preponderance, description, logo_image_path,
  cover_image_path, layout_diagram_path
 ) VALUES (
  p_complex_id, p_organization_id, btrim(p_complex->>'name'),
  btrim(p_complex->>'address'), p_complex->>'location',
  coalesce((p_complex->>'is_active')::boolean, true), p_complex->>'key',
  coalesce((p_complex->>'sort_order')::integer, 0),
  coalesce((p_complex->>'preponderance')::integer, 0),
  p_complex->>'description', p_complex->>'logo_image_path',
  p_complex->>'cover_image_path', p_complex->>'layout_diagram_path'
 )
 ON CONFLICT (id) DO UPDATE SET
  name = excluded.name,
  address = excluded.address,
  location = excluded.location,
  is_active = excluded.is_active,
  key = excluded.key,
  sort_order = excluded.sort_order,
  preponderance = excluded.preponderance,
  description = excluded.description,
  logo_image_path = excluded.logo_image_path,
  cover_image_path = excluded.cover_image_path,
  layout_diagram_path = excluded.layout_diagram_path,
  updated_at_utc = now()
 WHERE complexes.organization_id = excluded.organization_id
 RETURNING id INTO court_id;
 IF NOT FOUND THEN
  RAISE EXCEPTION 'complex belongs to another organization';
 END IF;
 FOR court IN SELECT value FROM jsonb_array_elements(p_courts) LOOP
  IF jsonb_typeof(court) <> 'object' OR nullif(btrim(court->>'name'), '') IS NULL THEN
   RAISE EXCEPTION 'court name required';
  END IF;
  court_id := coalesce(nullif(court->>'id','')::uuid, gen_random_uuid());
  IF court_id = ANY(seen_courts) OR court_id = ANY(p_delete_court_ids) THEN
   RAISE EXCEPTION 'duplicate or overlapping court id';
  END IF;
  seen_courts := array_append(seen_courts,court_id);
  returned_courts := array_append(returned_courts,court_id);
  IF EXISTS (
   SELECT 1 FROM public.courts
   WHERE id = court_id AND complex_id <> p_complex_id
  ) THEN
   RAISE EXCEPTION 'court belongs to another complex';
  END IF;
  IF court ? 'sport_ids' AND jsonb_typeof(court->'sport_ids') <> 'array' THEN
   RAISE EXCEPTION 'sport_ids must be an array';
  END IF;
  INSERT INTO public.courts (id, complex_id, name, is_active, surface_type, is_indoor)
  VALUES (
   court_id, p_complex_id, btrim(court->>'name'),
   coalesce((court->>'is_active')::boolean, true), court->>'surface_type',
   coalesce((court->>'is_indoor')::boolean, false)
  )
  ON CONFLICT (id) DO UPDATE SET
   name = excluded.name,
   is_active = excluded.is_active,
   surface_type = excluded.surface_type,
   is_indoor = excluded.is_indoor
  WHERE courts.complex_id = excluded.complex_id
  RETURNING id INTO court_id;
  IF NOT FOUND THEN
   RAISE EXCEPTION 'court belongs to another complex';
  END IF;
  IF court ? 'sport_ids' THEN
   seen_sports := '{}';
   DELETE FROM public.court_sports cs
   WHERE cs.court_id = seen_courts[array_length(seen_courts, 1)];
   FOR sport IN SELECT value FROM jsonb_array_elements(court->'sport_ids') LOOP
    sport_id := (sport #>> '{}')::uuid;
    IF sport_id IS NULL OR sport_id = ANY(seen_sports) THEN
     RAISE EXCEPTION 'duplicate or null sport id';
    END IF;
    seen_sports := array_append(seen_sports,sport_id);
    INSERT INTO public.court_sports (court_id,sport_id) VALUES (court_id,sport_id);
   END LOOP;
  END IF;
 END LOOP;
 FOREACH deleted_id IN ARRAY p_delete_court_ids LOOP
  IF NOT EXISTS (
   SELECT 1 FROM public.courts WHERE id = deleted_id AND complex_id = p_complex_id
  ) THEN
   RAISE EXCEPTION 'delete court does not belong to complex';
  END IF;
  DELETE FROM public.courts
  WHERE id = deleted_id AND complex_id = p_complex_id;
 END LOOP;
 SELECT count(*) INTO count_courts
 FROM public.courts WHERE complex_id = p_complex_id;
 RETURN jsonb_build_object(
  'complex_id', p_complex_id,
  'court_count', count_courts,
  'court_ids', to_jsonb(returned_courts)
 );
END $$;
REVOKE ALL ON FUNCTION public.save_complex_with_courts(uuid,uuid,jsonb,jsonb,uuid[]) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.save_complex_with_courts(uuid,uuid,jsonb,jsonb,uuid[]) FROM anon,authenticated;
GRANT EXECUTE ON FUNCTION public.save_complex_with_courts(uuid,uuid,jsonb,jsonb,uuid[]) TO service_role;
