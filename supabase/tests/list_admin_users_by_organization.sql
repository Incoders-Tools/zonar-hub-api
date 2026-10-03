-- Regression checks for public.list_admin_users_by_organization.
-- Assertion messages report only emails and counts; user rows carry password hashes.

DO $$
DECLARE rpc regprocedure := 'public.list_admin_users_by_organization(uuid,uuid,uuid,text,text,boolean,integer,integer)'::regprocedure;
BEGIN
 IF NOT has_function_privilege('service_role', rpc, 'EXECUTE')
    OR has_function_privilege('anon', rpc, 'EXECUTE')
    OR has_function_privilege('authenticated', rpc, 'EXECUTE')
 THEN
  RAISE EXCEPTION 'list RPC grants are not service-role-only';
 END IF;
 IF (SELECT prosecdef FROM pg_proc WHERE oid = rpc) THEN
  RAISE EXCEPTION 'list RPC must be SECURITY INVOKER';
 END IF;
 RAISE NOTICE 'list RPC service-role-only execute grants and SECURITY INVOKER: passed';
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
   PERFORM public.list_admin_users_by_organization('f1000000-0000-0000-0000-000000000000', NULL, NULL, NULL, NULL, NULL, 0, 10);
  EXCEPTION WHEN insufficient_privilege THEN
   denied := true;
  END;
  EXECUTE 'RESET ROLE';
  IF NOT denied THEN
   RAISE EXCEPTION '% list execution was not denied', role_name;
  END IF;
 END LOOP;
 RAISE NOTICE 'anon/authenticated list execution denied: passed';
END $$;

INSERT INTO public.tenants (id, name, key, contact_email, plan_type)
VALUES
 ('e1000000-0000-0000-0000-000000000000', 'Tenant one', 'aus-tenant-one', 'one@aus.test', 'pro'),
 ('e2000000-0000-0000-0000-000000000000', 'Tenant two', 'aus-tenant-two', 'two@aus.test', 'pro');
INSERT INTO public.organizations (id, tenant_id, display_name, type, created_by_user_id)
VALUES
 ('f1000000-0000-0000-0000-000000000000', 'e1000000-0000-0000-0000-000000000000', 'Org one', 'estandar', 'e1000000-0000-0000-0000-000000000001'),
 ('f2000000-0000-0000-0000-000000000000', 'e1000000-0000-0000-0000-000000000000', 'Org two', 'estandar', 'e1000000-0000-0000-0000-000000000001'),
 ('f3000000-0000-0000-0000-000000000000', 'e2000000-0000-0000-0000-000000000000', 'Org three', 'estandar', 'e2000000-0000-0000-0000-000000000001'),
 ('f4000000-0000-0000-0000-000000000000', 'e1000000-0000-0000-0000-000000000000', 'Org four', 'estandar', 'e1000000-0000-0000-0000-000000000001');
-- Tenant one: primary, secondary, assignment-only, other-org, unassigned, assigned-elsewhere,
-- inactive viewer, and a literal wildcard name. Tenant two: cross-tenant assignee and unassigned.
-- Org four mixes same-tenant system admins (primary, assignment-only, unassigned) with members.
INSERT INTO public.users (id, email, full_name, password_hash, role, role_id, tenant_id, organization_id, is_active)
VALUES
 ('d0000000-0000-0000-0000-000000000001', 'a@aus.test', 'Ana Primary', 'hash-a', 'admin', 'role002', 'e1000000-0000-0000-0000-000000000000', 'f1000000-0000-0000-0000-000000000000', true),
 ('d0000000-0000-0000-0000-000000000002', 'b@aus.test', 'Bruno Multi', 'hash-b', 'user', 'role005', 'e1000000-0000-0000-0000-000000000000', 'f2000000-0000-0000-0000-000000000000', true),
 ('d0000000-0000-0000-0000-000000000003', 'c@aus.test', 'Carla Assigned', 'hash-c', 'user', 'role005', 'e1000000-0000-0000-0000-000000000000', NULL, true),
 ('d0000000-0000-0000-0000-000000000004', 'd@aus.test', 'Dario Other', 'hash-d', 'user', 'role005', 'e1000000-0000-0000-0000-000000000000', 'f2000000-0000-0000-0000-000000000000', true),
 ('d0000000-0000-0000-0000-000000000005', 'e@aus.test', 'Eva Unassigned', 'hash-e', 'user', 'role005', 'e1000000-0000-0000-0000-000000000000', NULL, true),
 ('d0000000-0000-0000-0000-000000000006', 'f@aus.test', 'Fede Elsewhere', 'hash-f', 'user', 'role005', 'e1000000-0000-0000-0000-000000000000', NULL, true),
 ('d0000000-0000-0000-0000-000000000007', 'g@aus.test', 'Gina Inactive', 'hash-g', 'viewer', 'role003', 'e1000000-0000-0000-0000-000000000000', 'f1000000-0000-0000-0000-000000000000', false),
 ('d0000000-0000-0000-0000-000000000008', 'h@aus.test', '100% Pro', 'hash-h', 'user', 'role005', 'e1000000-0000-0000-0000-000000000000', 'f1000000-0000-0000-0000-000000000000', true),
 ('d0000000-0000-0000-0000-000000000009', 'x@aus.test', 'Xavi Foreign', 'hash-x', 'user', 'role005', 'e2000000-0000-0000-0000-000000000000', 'f3000000-0000-0000-0000-000000000000', true),
 ('d0000000-0000-0000-0000-000000000010', 'y@aus.test', 'Yago Unassigned', 'hash-y', 'user', 'role005', 'e2000000-0000-0000-0000-000000000000', NULL, true),
 ('d0000000-0000-0000-0000-000000000011', 'j@aus.test', 'Juan Root Unassigned', 'hash-j', 'system_admin', 'role001', 'e1000000-0000-0000-0000-000000000000', NULL, true),
 ('d0000000-0000-0000-0000-000000000012', 'k@aus.test', 'Kim Root Primary', 'hash-k', 'system_admin', 'role001', 'e1000000-0000-0000-0000-000000000000', 'f4000000-0000-0000-0000-000000000000', true),
 ('d0000000-0000-0000-0000-000000000013', 'l@aus.test', 'Lia Root Assigned', 'hash-l', 'system_admin', 'role001', 'e1000000-0000-0000-0000-000000000000', NULL, true),
 ('d0000000-0000-0000-0000-000000000014', 'm@aus.test', 'Mia Primary', 'hash-m', 'user', 'role005', 'e1000000-0000-0000-0000-000000000000', 'f4000000-0000-0000-0000-000000000000', true),
 ('d0000000-0000-0000-0000-000000000015', 'n@aus.test', 'Nora Assigned', 'hash-n', 'user', 'role005', 'e1000000-0000-0000-0000-000000000000', NULL, true);
INSERT INTO public.user_organization_assignments (user_id, organization_id)
VALUES
 ('d0000000-0000-0000-0000-000000000001', 'f1000000-0000-0000-0000-000000000000'),
 ('d0000000-0000-0000-0000-000000000002', 'f1000000-0000-0000-0000-000000000000'),
 ('d0000000-0000-0000-0000-000000000002', 'f2000000-0000-0000-0000-000000000000'),
 ('d0000000-0000-0000-0000-000000000003', 'f1000000-0000-0000-0000-000000000000'),
 ('d0000000-0000-0000-0000-000000000006', 'f2000000-0000-0000-0000-000000000000'),
 ('d0000000-0000-0000-0000-000000000009', 'f1000000-0000-0000-0000-000000000000'),
 ('d0000000-0000-0000-0000-000000000013', 'f4000000-0000-0000-0000-000000000000'),
 ('d0000000-0000-0000-0000-000000000015', 'f4000000-0000-0000-0000-000000000000');

CREATE FUNCTION pg_temp.assert_page(label text, result jsonb, expected_total integer, expected_emails text)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE actual_emails text;
BEGIN
 SELECT coalesce(string_agg(item->>'email', ',' ORDER BY position), '')
 INTO actual_emails
 FROM jsonb_array_elements(result->'items') WITH ORDINALITY AS page(item, position);
 IF (result->>'total_count')::integer IS DISTINCT FROM expected_total
    OR actual_emails IS DISTINCT FROM expected_emails THEN
  RAISE EXCEPTION '%: expected total % [%], got total % [%]',
   label, expected_total, expected_emails, result->>'total_count', actual_emails;
 END IF;
END $$;

SET ROLE service_role;
DO $$
DECLARE
 org_one constant uuid := 'f1000000-0000-0000-0000-000000000000';
 org_two constant uuid := 'f2000000-0000-0000-0000-000000000000';
 org_four constant uuid := 'f4000000-0000-0000-0000-000000000000';
 tenant_one constant uuid := 'e1000000-0000-0000-0000-000000000000';
 result jsonb;
BEGIN
 -- System admin in organization mode: primary OR assignment, no duplicates, no unassigned users.
 result := public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, NULL, NULL, 0, 50);
 PERFORM pg_temp.assert_page('sysadmin membership', result, 6, 'a@aus.test,b@aus.test,c@aus.test,g@aus.test,h@aus.test,x@aus.test');
 IF NOT (result->'items'->0 ?& ARRAY['id', 'password_hash', 'role_id', 'tenant_id', 'organization_id', 'created_at_utc']) THEN
  RAISE EXCEPTION 'items are not complete user rows';
 END IF;

 -- Filtering happens before paging; count stays accurate on every page.
 PERFORM pg_temp.assert_page('page 1', public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, NULL, NULL, 0, 2), 6, 'a@aus.test,b@aus.test');
 PERFORM pg_temp.assert_page('page 2', public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, NULL, NULL, 2, 2), 6, 'c@aus.test,g@aus.test');
 PERFORM pg_temp.assert_page('page 3', public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, NULL, NULL, 4, 2), 6, 'h@aus.test,x@aus.test');
 PERFORM pg_temp.assert_page('out-of-range page', public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, NULL, NULL, 40, 2), 6, '');

 -- Multi-organization user appears in both organizations.
 PERFORM pg_temp.assert_page('second organization', public.list_admin_users_by_organization(org_two, NULL, NULL, NULL, NULL, NULL, 0, 50), 3, 'b@aus.test,d@aus.test,f@aus.test');

 -- Tenant isolation drops the cross-tenant assignee.
 PERFORM pg_temp.assert_page('tenant filter', public.list_admin_users_by_organization(org_one, tenant_one, NULL, NULL, NULL, NULL, 0, 50), 5, 'a@aus.test,b@aus.test,c@aus.test,g@aus.test,h@aus.test');

 -- Tenant admin exception adds only truly unassigned users of that tenant.
 PERFORM pg_temp.assert_page('tenant admin unassigned', public.list_admin_users_by_organization(org_one, tenant_one, tenant_one, NULL, NULL, NULL, 0, 50), 6, 'a@aus.test,b@aus.test,c@aus.test,e@aus.test,g@aus.test,h@aus.test');
 PERFORM pg_temp.assert_page('tenant admin unassigned paged', public.list_admin_users_by_organization(org_one, tenant_one, tenant_one, NULL, NULL, NULL, 3, 1), 6, 'e@aus.test');

 -- Search is case-insensitive and treats LIKE wildcards literally.
 PERFORM pg_temp.assert_page('search name', public.list_admin_users_by_organization(org_one, NULL, NULL, 'MULTI', NULL, NULL, 0, 50), 1, 'b@aus.test');
 PERFORM pg_temp.assert_page('search email', public.list_admin_users_by_organization(org_one, NULL, NULL, ' X@AUS ', NULL, NULL, 0, 50), 1, 'x@aus.test');
 PERFORM pg_temp.assert_page('search percent', public.list_admin_users_by_organization(org_one, NULL, NULL, '%', NULL, NULL, 0, 50), 1, 'h@aus.test');
 PERFORM pg_temp.assert_page('search underscore', public.list_admin_users_by_organization(org_one, NULL, NULL, '_', NULL, NULL, 0, 50), 0, '');
 PERFORM pg_temp.assert_page('blank search', public.list_admin_users_by_organization(org_one, tenant_one, NULL, '   ', NULL, NULL, 0, 50), 5, 'a@aus.test,b@aus.test,c@aus.test,g@aus.test,h@aus.test');

 -- Role and active filters combine with membership.
 PERFORM pg_temp.assert_page('role filter', public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, 'viewer', NULL, 0, 50), 1, 'g@aus.test');
 PERFORM pg_temp.assert_page('inactive filter', public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, NULL, false, 0, 50), 1, 'g@aus.test');
 PERFORM pg_temp.assert_page('active filter', public.list_admin_users_by_organization(org_one, tenant_one, tenant_one, NULL, 'user', true, 0, 50), 4, 'b@aus.test,c@aus.test,e@aus.test,h@aus.test');

 -- Tenant-scoped lists exclude same-tenant system admins before counting and paging;
 -- the unscoped system admin list keeps them.
 PERFORM pg_temp.assert_page('sysadmin sees system admins', public.list_admin_users_by_organization(org_four, NULL, NULL, NULL, NULL, NULL, 0, 50), 4, 'k@aus.test,l@aus.test,m@aus.test,n@aus.test');
 PERFORM pg_temp.assert_page('sysadmin system admin role', public.list_admin_users_by_organization(org_four, NULL, NULL, NULL, 'system_admin', NULL, 0, 50), 2, 'k@aus.test,l@aus.test');
 PERFORM pg_temp.assert_page('tenant filter hides system admins', public.list_admin_users_by_organization(org_four, tenant_one, NULL, NULL, NULL, NULL, 0, 50), 2, 'm@aus.test,n@aus.test');
 PERFORM pg_temp.assert_page('tenant admin hides system admins page 1', public.list_admin_users_by_organization(org_four, tenant_one, tenant_one, NULL, NULL, NULL, 0, 2), 3, 'e@aus.test,m@aus.test');
 PERFORM pg_temp.assert_page('tenant admin hides system admins page 2', public.list_admin_users_by_organization(org_four, tenant_one, tenant_one, NULL, NULL, NULL, 2, 2), 3, 'n@aus.test');
 PERFORM pg_temp.assert_page('tenant admin system admin role', public.list_admin_users_by_organization(org_four, tenant_one, tenant_one, NULL, 'system_admin', NULL, 0, 50), 0, '');
 PERFORM pg_temp.assert_page('tenant admin system admin search', public.list_admin_users_by_organization(org_four, tenant_one, tenant_one, 'root', NULL, NULL, 0, 50), 0, '');

 -- Invalid arguments fail instead of returning an unscoped or unbounded list.
 BEGIN
  PERFORM public.list_admin_users_by_organization(NULL, NULL, NULL, NULL, NULL, NULL, 0, 10);
  RAISE EXCEPTION 'null organization accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'organization id required' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, NULL, NULL, -1, 10);
  RAISE EXCEPTION 'negative offset accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid page' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.list_admin_users_by_organization(org_one, NULL, NULL, NULL, NULL, NULL, 0, 0);
  RAISE EXCEPTION 'empty limit accepted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'invalid page' THEN RAISE; END IF;
 END;
 RAISE NOTICE 'membership, paging/count, multi-org, tenant isolation, unassigned exception, filters, literal search, tenant system admin exclusion, invalid args: passed';
END $$;
RESET ROLE;
