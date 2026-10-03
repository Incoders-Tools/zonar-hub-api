-- Organization-scoped admin user list. Membership is the user's primary organization OR an
-- organization assignment; filtering and counting happen before paging so totals stay accurate.
CREATE FUNCTION public.list_admin_users_by_organization(
 p_organization_id uuid,
 p_tenant_id uuid,
 p_include_unassigned_tenant_id uuid,
 p_search text,
 p_role text,
 p_is_active boolean,
 p_offset integer,
 p_limit integer)
RETURNS jsonb LANGUAGE plpgsql STABLE SECURITY INVOKER SET search_path = public, pg_temp AS $$
DECLARE
 -- strpos matches the search literally, so LIKE wildcards in user input carry no meaning.
 needle text := lower(nullif(btrim(p_search), ''));
 total integer;
 page jsonb;
BEGIN
 -- Only the service role may execute this RPC. The API validates the caller and the organization
 -- scope before invoking it; this function does not authorize users.
 IF p_organization_id IS NULL THEN
  RAISE EXCEPTION 'organization id required';
 END IF;
 IF p_offset IS NULL OR p_offset < 0 OR p_limit IS NULL OR p_limit < 1 THEN
  RAISE EXCEPTION 'invalid page';
 END IF;

 WITH members AS (
  SELECT u.*
  FROM public.users u
  WHERE (p_tenant_id IS NULL OR u.tenant_id = p_tenant_id)
    AND (
     u.organization_id = p_organization_id
     OR EXISTS (
      SELECT 1 FROM public.user_organization_assignments a
      WHERE a.user_id = u.id AND a.organization_id = p_organization_id
     )
     -- Tenant administrators also see users of their tenant with no organization at all.
     OR (
      p_include_unassigned_tenant_id IS NOT NULL
      AND u.tenant_id = p_include_unassigned_tenant_id
      AND u.organization_id IS NULL
      AND NOT EXISTS (
       SELECT 1 FROM public.user_organization_assignments a WHERE a.user_id = u.id
      )
     )
    )
    AND (needle IS NULL OR strpos(lower(u.email), needle) > 0 OR strpos(lower(u.full_name), needle) > 0)
    AND (p_role IS NULL OR u.role = p_role)
    AND (p_is_active IS NULL OR u.is_active = p_is_active)
 )
 SELECT
  (SELECT count(*) FROM members),
  coalesce(
   (SELECT jsonb_agg(to_jsonb(paged) ORDER BY paged.email, paged.id)
    FROM (SELECT * FROM members ORDER BY email, id OFFSET p_offset LIMIT p_limit) paged),
   '[]'::jsonb)
 INTO total, page;

 RETURN jsonb_build_object('total_count', total, 'items', page);
END $$;
REVOKE ALL ON FUNCTION public.list_admin_users_by_organization(uuid,uuid,uuid,text,text,boolean,integer,integer) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.list_admin_users_by_organization(uuid,uuid,uuid,text,text,boolean,integer,integer) FROM anon,authenticated;
GRANT EXECUTE ON FUNCTION public.list_admin_users_by_organization(uuid,uuid,uuid,text,text,boolean,integer,integer) TO service_role;
