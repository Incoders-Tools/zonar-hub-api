#!/usr/bin/env bash
set -euo pipefail
# MSYS must not rewrite container-only /tmp paths into Windows host paths.
export MSYS_NO_PATHCONV=1

cd "$(dirname "$0")/../.."
container="$(docker run -d --rm -e POSTGRES_HOST_AUTH_METHOD=trust postgres:17)"
control_dir=''
cleanup() {
  if [[ -n "$control_dir" ]]; then
    # Release a waiting transaction before stopping the disposable server.
    docker exec "$container" touch "$control_dir/release" >/dev/null 2>&1 || true
  fi
  docker stop "$container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

for attempt in {1..60}; do
  if docker exec "$container" pg_isready -U postgres >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec "$container" pg_isready -U postgres >/dev/null

# Migrations expect the Supabase login roles, but no synthetic application tables.
docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres <<'SQL'
CREATE ROLE service_role LOGIN BYPASSRLS;
CREATE ROLE authenticated LOGIN;
CREATE ROLE anon LOGIN;
-- Schema usage lets privilege probes reach table and function ACLs.
GRANT USAGE ON SCHEMA public TO anon, authenticated, service_role;
SQL

# REQ-AUD-016 and session secrecy: application roles hold only the intended
# table privileges. RLS does not stop TRUNCATE, and service_role bypasses RLS,
# so both catalog ACLs and real statements are probed. Synthetic UUIDs only.
probe_impersonation_privileges() {
  docker exec -i "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres <<'SQL'
BEGIN;
DO $$
DECLARE
    probe record;
    allowed boolean;
    visible bigint;
BEGIN
    FOR probe IN
        SELECT role_name, rel, privilege
          FROM unnest(ARRAY['anon', 'authenticated', 'service_role']) role_name,
               unnest(ARRAY['public.impersonation_sessions', 'public.impersonation_audit']) rel,
               unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER']) privilege
    LOOP
        IF has_table_privilege(probe.role_name, probe.rel, probe.privilege)
           IS DISTINCT FROM (probe.role_name = 'service_role' AND (probe.privilege IN ('SELECT', 'INSERT')
               OR (probe.privilege = 'UPDATE' AND probe.rel = 'public.impersonation_sessions'))) THEN
            RAISE EXCEPTION '% % privilege for % is %', probe.rel, probe.privilege, probe.role_name,
                has_table_privilege(probe.role_name, probe.rel, probe.privilege);
        END IF;
    END LOOP;
    -- Ordered: service_role first creates the session the audit row references.
    FOR probe IN SELECT * FROM (VALUES
        ('service_role', 'INSERT INTO public.impersonation_sessions (id, real_user_id, target_user_id, started_at, expires_at, tenant_id) '
         || 'VALUES (''a0000000-0000-0000-0000-000000000001'', ''a0000000-0000-0000-0000-000000000002'', '
         || '''a0000000-0000-0000-0000-000000000003'', now(), now() + interval ''1 hour'', '
         || '''a0000000-0000-0000-0000-000000000004'')', true),
        ('service_role', 'SELECT 1 FROM public.impersonation_sessions', true),
        ('service_role', 'UPDATE public.impersonation_sessions SET revoked_at = now() '
         || 'WHERE id = ''a0000000-0000-0000-0000-000000000001''', true),
        ('service_role', 'DELETE FROM public.impersonation_sessions', false),
        ('service_role', 'TRUNCATE public.impersonation_sessions', false),
        ('service_role', 'INSERT INTO public.impersonation_audit (id, session_id, real_user_id, effective_user_id, method, path, event_type) '
         || 'VALUES (''a0000000-0000-0000-0000-000000000005'', ''a0000000-0000-0000-0000-000000000001'', '
         || '''a0000000-0000-0000-0000-000000000002'', ''a0000000-0000-0000-0000-000000000003'', ''GET'', ''/probe'', ''request'')', true),
        ('service_role', 'SELECT 1 FROM public.impersonation_audit', true),
        ('service_role', 'UPDATE public.impersonation_audit SET path = ''/tampered''', false),
        ('service_role', 'DELETE FROM public.impersonation_audit', false),
        ('service_role', 'TRUNCATE public.impersonation_audit', false),
        ('anon', 'SELECT 1 FROM public.impersonation_sessions', false),
        ('anon', 'INSERT INTO public.impersonation_sessions DEFAULT VALUES', false),
        ('anon', 'UPDATE public.impersonation_sessions SET revoked_at = now()', false),
        ('anon', 'DELETE FROM public.impersonation_sessions', false),
        ('anon', 'TRUNCATE public.impersonation_sessions', false),
        ('anon', 'SELECT 1 FROM public.impersonation_audit', false),
        ('anon', 'INSERT INTO public.impersonation_audit DEFAULT VALUES', false),
        ('anon', 'UPDATE public.impersonation_audit SET path = ''/tampered''', false),
        ('anon', 'DELETE FROM public.impersonation_audit', false),
        ('anon', 'TRUNCATE public.impersonation_audit', false),
        ('authenticated', 'SELECT 1 FROM public.impersonation_sessions', false),
        ('authenticated', 'INSERT INTO public.impersonation_sessions DEFAULT VALUES', false),
        ('authenticated', 'UPDATE public.impersonation_sessions SET revoked_at = now()', false),
        ('authenticated', 'DELETE FROM public.impersonation_sessions', false),
        ('authenticated', 'TRUNCATE public.impersonation_sessions', false),
        ('authenticated', 'SELECT 1 FROM public.impersonation_audit', false),
        ('authenticated', 'INSERT INTO public.impersonation_audit DEFAULT VALUES', false),
        ('authenticated', 'UPDATE public.impersonation_audit SET path = ''/tampered''', false),
        ('authenticated', 'DELETE FROM public.impersonation_audit', false),
        ('authenticated', 'TRUNCATE public.impersonation_audit', false)
    ) AS probes(role_name, statement, expected) LOOP
        BEGIN
            EXECUTE format('SET LOCAL ROLE %I', probe.role_name);
            EXECUTE probe.statement;
            allowed := true;
        EXCEPTION
            WHEN insufficient_privilege THEN
                allowed := false;
            -- The privilege check passed; only the audit foreign key blocked the row removal.
            WHEN foreign_key_violation OR feature_not_supported THEN
                allowed := true;
        END;
        RESET ROLE;
        IF allowed IS DISTINCT FROM probe.expected THEN
            RAISE EXCEPTION 'impersonation % for %: allowed=%, expected=%',
                probe.statement, probe.role_name, allowed, probe.expected;
        END IF;
    END LOOP;
    SET LOCAL ROLE service_role;
    SELECT count(*) INTO visible FROM public.impersonation_audit a
      JOIN public.impersonation_sessions s ON s.id = a.session_id
     WHERE a.path = '/probe' AND s.revoked_at IS NOT NULL;
    RESET ROLE;
    IF visible <> 1 THEN
        RAISE EXCEPTION 'service_role cannot read its revoked session audit row: %', visible;
    END IF;
END $$;
ROLLBACK;
SQL
}

# court_sports is reached only by the service-role RPC: service_role holds plain
# DML (not TRUNCATE), anon/authenticated hold nothing. Synthetic UUIDs only.
probe_court_sports_privileges() {
  docker exec -i "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres <<'SQL'
BEGIN;
DO $$
DECLARE
    probe record;
    allowed boolean;
BEGIN
    FOR probe IN
        SELECT role_name, privilege
          FROM unnest(ARRAY['anon', 'authenticated', 'service_role']) role_name,
               unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER', 'MAINTAIN']) privilege
    LOOP
        IF has_table_privilege(probe.role_name, 'public.court_sports', probe.privilege)
           IS DISTINCT FROM (probe.role_name = 'service_role'
               AND probe.privilege IN ('SELECT', 'INSERT', 'UPDATE', 'DELETE')) THEN
            RAISE EXCEPTION 'public.court_sports % privilege for % is %', probe.privilege, probe.role_name,
                has_table_privilege(probe.role_name, 'public.court_sports', probe.privilege);
        END IF;
    END LOOP;
    FOR probe IN
        SELECT role_name, statement, role_name = 'service_role' AND statement NOT LIKE 'TRUNCATE%' AS expected
          FROM unnest(ARRAY['service_role', 'anon', 'authenticated']) role_name,
               unnest(ARRAY[
                   'SELECT 1 FROM public.court_sports',
                   'INSERT INTO public.court_sports (court_id, sport_id) VALUES '
                   || '(''c0000000-0000-0000-0000-000000000001'', ''c0000000-0000-0000-0000-000000000002'')',
                   'UPDATE public.court_sports SET sport_id = sport_id',
                   'DELETE FROM public.court_sports',
                   'TRUNCATE public.court_sports']) statement
    LOOP
        BEGIN
            EXECUTE format('SET LOCAL ROLE %I', probe.role_name);
            EXECUTE probe.statement;
            allowed := true;
        EXCEPTION
            WHEN insufficient_privilege THEN
                allowed := false;
            -- The privilege check passed; only the synthetic row's foreign keys failed.
            WHEN foreign_key_violation THEN
                allowed := true;
        END;
        RESET ROLE;
        IF allowed IS DISTINCT FROM probe.expected THEN
            RAISE EXCEPTION 'court_sports % for %: allowed=%, expected=%',
                probe.statement, probe.role_name, allowed, probe.expected;
        END IF;
    END LOOP;
END $$;
ROLLBACK;
SQL
}

# Hosted Supabase grants every new public table to the API roles by default.
# Emulate that only around the first apply of the two migrations that create
# tables, so the other files keep their explicit limited-grant signal.
for migration in supabase/migrations/*.sql; do
  hosted_defaults=false
  if [[ "$migration" == */20260515_create_impersonation_tables.sql ||
        "$migration" == */20261001010000_save_complex_with_courts.sql ]]; then
    hosted_defaults=true
    docker exec "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres -c \
      'ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO anon, authenticated, service_role;'
  fi
  printf 'Applying %s\n' "$migration"
  docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres < "$migration"
  if [[ "$hosted_defaults" == true ]]; then
    docker exec "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres -c \
      'ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM anon, authenticated, service_role;'
    [[ "$(docker exec "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres -c 'SELECT count(*) FROM pg_default_acl')" == 0 ]]
  fi
  if [[ "$migration" == */20260515_create_impersonation_tables.sql ]]; then
    probe_impersonation_privileges
    echo 'First apply under hosted default grants leaves least-privilege impersonation ACLs: passed'
  elif [[ "$migration" == */20261001010000_save_complex_with_courts.sql ]]; then
    probe_court_sports_privileges
    echo 'First apply under hosted default grants leaves least-privilege court_sports ACLs: passed'
  fi
done

# Catalog fingerprint of every object owned by the two replayable migrations.
# A pinned search_path keeps renderings schema-qualified and deterministic.
replay_snapshot() {
  docker exec -i "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres <<'SQL'
SET search_path = pg_catalog;
WITH rels(rel) AS (VALUES ('public.impersonation_sessions'::regclass), ('public.impersonation_audit'::regclass),
                          ('public.courts'::regclass), ('public.court_sports'::regclass))
SELECT format('column %s.%s %s notnull=%s default=%s', a.attrelid::regclass, a.attname,
              format_type(a.atttypid, a.atttypmod), a.attnotnull, pg_get_expr(d.adbin, d.adrelid))
  FROM pg_attribute a JOIN rels ON a.attrelid = rels.rel
  LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
 WHERE a.attnum > 0 AND NOT a.attisdropped
UNION ALL SELECT format('constraint %s %s', conrelid::regclass, pg_get_constraintdef(oid))
  FROM pg_constraint WHERE conrelid IN (SELECT rel FROM rels)
UNION ALL SELECT format('index %s', pg_get_indexdef(indexrelid)) FROM pg_index WHERE indrelid IN (SELECT rel FROM rels)
UNION ALL SELECT format('table %s rls=%s force=%s acl=%s', oid::regclass, relrowsecurity, relforcerowsecurity, relacl)
  FROM pg_class WHERE oid IN (SELECT rel FROM rels)
UNION ALL SELECT format('policy %s %s permissive=%s cmd=%s roles=%s using=%s check=%s', polrelid::regclass, polname,
              polpermissive, polcmd, polroles::regrole[], pg_get_expr(polqual, polrelid), pg_get_expr(polwithcheck, polrelid))
  FROM pg_policy WHERE polrelid IN (SELECT rel FROM rels)
UNION ALL SELECT format('function %s definer=%s acl=%s %s', oid::regprocedure, prosecdef, proacl, pg_get_functiondef(oid))
  FROM pg_proc WHERE proname = 'save_complex_with_courts'
ORDER BY 1;
SQL
}

# Pending migrations must be replayable on the schema they produced, without
# duplicate objects or any change to columns, RLS, policies, grants or the RPC.
replayable=(
  supabase/migrations/20260515_create_impersonation_tables.sql
  supabase/migrations/20261001010000_save_complex_with_courts.sql
)
before_replay="$(replay_snapshot)"
for migration in "${replayable[@]}"; do
  printf 'Replaying %s\n' "$migration"
  docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres < "$migration"
done
if [[ "$(replay_snapshot)" != "$before_replay" ]]; then
  echo 'Replay changed the migrated schema' >&2
  diff <(printf '%s\n' "$before_replay") <(replay_snapshot) >&2 || true
  exit 1
fi
echo 'Replay of pending migrations preserved schema, RLS, policies, grants and RPC: passed'

# Replay must not widen the impersonation ACLs left by the first apply.
probe_impersonation_privileges
echo 'Impersonation sessions are select/insert/update-only and audit insert/select-only for service_role, closed to anon/authenticated: passed'
probe_court_sports_privileges
echo 'court_sports is plain DML-only for service_role and closed to anon/authenticated after replay: passed'

# Full seed rows, including updated_at_utc, of the catalogs the eight pending
# data migrations upsert, delete or update.
seed_snapshot() {
  docker exec -i "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres <<'SQL'
SELECT 'roles ' || r::text FROM public.roles r
UNION ALL SELECT 'system_modules ' || m::text FROM public.system_modules m
UNION ALL SELECT 'system_tools ' || t::text FROM public.system_tools t
UNION ALL SELECT 'tournament_statuses ' || s::text FROM public.tournament_statuses s
UNION ALL SELECT 'tournament_rules ' || r::text FROM public.tournament_rules r
ORDER BY 1;
SQL
}

# Replaying the eight pending data migrations in order must leave every seeded
# row untouched, timestamps included. The pause guarantees a later NOW().
seed_replayable=(
  20260506074824_create_roles_and_user_role_fk 20260506081306_add_user_org_permissions
  20260506120000_cleanup_orphaned_tenants 20260506130000_remove_duplicate_tenants_tool
  20260507000000_create_tournament_statuses_table 20260507100000_open_plans_and_billing_to_admins
  20260507200000_extend_tournaments_table 20260508120000_create_tournament_rules_table
)
before_seed_replay="$(seed_snapshot)"
sleep 0.1
for version in "${seed_replayable[@]}"; do
  printf 'Replaying %s\n' "$version"
  docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres < "supabase/migrations/$version.sql"
  # Admins keep plans/billing between replayed files, not only at the end.
  access="$(docker exec "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres -c \
    "SELECT string_agg(is_system_admin_only::text, ',' ORDER BY key) FROM public.system_tools WHERE key IN ('billing', 'plans')")"
  if [[ "$access" != 'false,false' ]]; then
    echo "Replay of $version restricted billing,plans to system admins: $access" >&2
    exit 1
  fi
done
if [[ "$(seed_snapshot)" != "$before_seed_replay" ]]; then
  echo 'Replay changed seeded catalog rows' >&2
  diff <(printf '%s\n' "$before_seed_replay") <(seed_snapshot) >&2 || true
  exit 1
fi
# roles|modules|tools|statuses|rules|billing,plans system-admin-only|tenants tools
seed_counts="$(docker exec -i "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres <<'SQL'
SELECT concat_ws('|', (SELECT count(*) FROM public.roles), (SELECT count(*) FROM public.system_modules),
  (SELECT count(*) FROM public.system_tools), (SELECT count(*) FROM public.tournament_statuses),
  (SELECT count(*) FROM public.tournament_rules),
  (SELECT string_agg(is_system_admin_only::text, ',' ORDER BY key) FROM public.system_tools WHERE key IN ('billing', 'plans')),
  (SELECT count(*) FROM public.system_tools WHERE key = 'tenants'));
SQL
)"
if [[ "$seed_counts" != '6|4|26|4|3|false,false|0' ]]; then
  echo "Unexpected seeded catalog state after replay: $seed_counts" >&2
  exit 1
fi
echo 'Replay of pending data migrations preserved seeded rows, counts, plans/billing access and tenants removal: passed'

# Reused object names with a different definition must fail closed. Each drift
# is introduced inside a transaction that psql abandons when the replay errors.
expect_drift_rejected() {
  local drift="$1" migration="$2" output
  if output="$({ printf 'BEGIN;\n%s\n' "$drift"; cat "$migration"; printf 'ROLLBACK;\n'; } |
      docker exec -i "$container" psql -X -v ON_ERROR_STOP=1 -U postgres 2>&1)"; then
    echo "Drift accepted by $migration: $drift" >&2
    exit 1
  fi
  if ! grep -q 'schema drift' <<<"$output"; then
    echo "Unexpected replay failure for $migration: $output" >&2
    exit 1
  fi
}
expect_drift_rejected 'ALTER POLICY "Service role insert on impersonation_audit" ON public.impersonation_audit TO service_role, anon;' \
  supabase/migrations/20260515_create_impersonation_tables.sql
expect_drift_rejected 'ALTER TABLE public.impersonation_sessions DROP CONSTRAINT impersonation_sessions_reason_check;' \
  supabase/migrations/20260515_create_impersonation_tables.sql
expect_drift_rejected 'DROP POLICY "Service role insert on impersonation_audit" ON public.impersonation_audit; CREATE POLICY "Service role insert on impersonation_audit" ON public.impersonation_audit TO service_role USING (true) WITH CHECK (true);' \
  supabase/migrations/20260515_create_impersonation_tables.sql
expect_drift_rejected 'CREATE ROLE audit_drift_probe; GRANT UPDATE ON public.impersonation_audit TO audit_drift_probe;' \
  supabase/migrations/20260515_create_impersonation_tables.sql
expect_drift_rejected 'CREATE ROLE audit_drift_probe; GRANT UPDATE (path) ON public.impersonation_audit TO audit_drift_probe;' \
  supabase/migrations/20260515_create_impersonation_tables.sql
expect_drift_rejected 'CREATE ROLE audit_drift_probe; GRANT TRUNCATE ON public.impersonation_sessions TO audit_drift_probe;' \
  supabase/migrations/20260515_create_impersonation_tables.sql
expect_drift_rejected 'CREATE ROLE audit_drift_probe; GRANT SELECT (target_user_id) ON public.impersonation_sessions TO audit_drift_probe;' \
  supabase/migrations/20260515_create_impersonation_tables.sql
expect_drift_rejected 'ALTER TABLE public.courts ALTER COLUMN is_indoor DROP NOT NULL;' \
  supabase/migrations/20261001010000_save_complex_with_courts.sql
expect_drift_rejected 'CREATE POLICY "Open read" ON public.court_sports FOR SELECT TO anon USING (true);' \
  supabase/migrations/20261001010000_save_complex_with_courts.sql
expect_drift_rejected 'ALTER TABLE public.court_sports DROP CONSTRAINT court_sports_court_id_fkey, ADD FOREIGN KEY (court_id) REFERENCES public.courts(id);' \
  supabase/migrations/20261001010000_save_complex_with_courts.sql
expect_drift_rejected 'CREATE ROLE court_drift_probe; GRANT TRUNCATE ON public.court_sports TO court_drift_probe;' \
  supabase/migrations/20261001010000_save_complex_with_courts.sql
expect_drift_rejected 'CREATE ROLE court_drift_probe; GRANT UPDATE (sport_id) ON public.court_sports TO court_drift_probe;' \
  supabase/migrations/20261001010000_save_complex_with_courts.sql
[[ "$(replay_snapshot)" == "$before_replay" ]]
echo 'Incompatible pre-existing definitions rejected on replay: passed'

# Disabled RLS is restored by the replay instead of being reported as drift.
{ printf 'BEGIN;\nALTER TABLE public.court_sports DISABLE ROW LEVEL SECURITY;\n'
  cat supabase/migrations/20261001010000_save_complex_with_courts.sql
  printf "SELECT relrowsecurity FROM pg_class WHERE oid = 'public.court_sports'::regclass;\nROLLBACK;\n"
} | docker exec -i "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres | grep -Fx t >/dev/null
echo 'Replay restores disabled court_sports RLS: passed'

# Broad grants to the API roles are revoked by the replay, like disabled RLS.
{ printf 'BEGIN;\nGRANT ALL ON public.impersonation_audit, public.impersonation_sessions TO anon, authenticated, service_role;\n'
  cat supabase/migrations/20260515_create_impersonation_tables.sql
  printf "SELECT has_table_privilege('service_role', 'public.impersonation_audit', 'UPDATE, DELETE, TRUNCATE') OR has_table_privilege('anon', 'public.impersonation_audit', 'SELECT, INSERT') OR has_table_privilege('authenticated', 'public.impersonation_audit', 'SELECT, INSERT') OR has_table_privilege('service_role', 'public.impersonation_sessions', 'DELETE, TRUNCATE') OR has_table_privilege('anon', 'public.impersonation_sessions', 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE') OR has_table_privilege('authenticated', 'public.impersonation_sessions', 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE');\nROLLBACK;\n"
} | docker exec -i "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres | grep -Fx f >/dev/null
echo 'Replay revokes broad impersonation grants: passed'

{ printf 'BEGIN;\nGRANT ALL ON public.court_sports TO PUBLIC;\nGRANT ALL ON public.court_sports TO anon, authenticated, service_role WITH GRANT OPTION;\nGRANT SELECT (sport_id) ON public.court_sports TO anon, authenticated;\n'
  cat supabase/migrations/20261001010000_save_complex_with_courts.sql
  printf "SELECT has_table_privilege('public', 'public.court_sports', 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER, MAINTAIN') OR has_table_privilege('service_role', 'public.court_sports', 'TRUNCATE, REFERENCES, TRIGGER, MAINTAIN') OR has_table_privilege('service_role', 'public.court_sports', 'SELECT WITH GRANT OPTION') OR has_table_privilege('anon', 'public.court_sports', 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER, MAINTAIN') OR has_table_privilege('authenticated', 'public.court_sports', 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES, TRIGGER, MAINTAIN') OR has_any_column_privilege('anon', 'public.court_sports', 'SELECT') OR has_any_column_privilege('authenticated', 'public.court_sports', 'SELECT') OR NOT (has_table_privilege('service_role', 'public.court_sports', 'SELECT') AND has_table_privilege('service_role', 'public.court_sports', 'INSERT') AND has_table_privilege('service_role', 'public.court_sports', 'UPDATE') AND has_table_privilege('service_role', 'public.court_sports', 'DELETE'));\nROLLBACK;\n"
} | docker exec -i "$container" psql -XAt -v ON_ERROR_STOP=1 -U postgres | grep -Fx f >/dev/null
echo 'Replay revokes broad court_sports grants: passed'

# Supabase grants its bypass-RLS service role access to application relations.
# Keep the harness grants limited to the aggregate RPC's fixture and write tables.
docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres <<'SQL'
GRANT USAGE ON SCHEMA public TO service_role;
GRANT SELECT ON public.organizations, public.sports TO service_role;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.complexes, public.courts TO service_role;
SQL

docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres < supabase/tests/complex_courts_atomic.sql

if [[ "${UNSAFE_COMPLEX_CONFLICT_TEST:-}" == 1 ]]; then
  # Mutation test: replace only the function in the disposable database, after
  # the existing sequential checks; never alter the migration on disk.
  awk '/^CREATE OR REPLACE FUNCTION public.save_complex_with_courts\(/ { copying=1 } copying { print } /^END \$\$;/ && copying { exit }' \
    supabase/migrations/20261001010000_save_complex_with_courts.sql |
    sed '/^ WHERE complexes.organization_id = excluded.organization_id$/d' |
    docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres
fi

# Both clients connect to the same private, disposable PostgreSQL instance.
# The first client keeps its transaction open until the controller explicitly
# releases it; a timed sleep cannot prove the conflict actually occurred.
control_dir="$(docker exec "$container" mktemp -d)"
docker exec "$container" chmod 700 "$control_dir"
docker exec -d "$container" bash -c '
  { printf "%s\n" "BEGIN;" "INSERT INTO public.complexes (id, organization_id, name, address) VALUES ('\''99999999-9999-9999-9999-999999999999'\'', '\''bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'\'', '\''Concurrent Beta'\'', '\''Street'\'');";
    printf "\\! touch %s/ready\n" "$1";
    while [[ ! -e "$1/release" ]]; do sleep 0.1; done;
    printf "%s\n" "COMMIT;";
  } | psql -X -v ON_ERROR_STOP=1 -U postgres >"$1/a.log" 2>&1
  echo $? >"$1/a.exit"
' bash "$control_dir"

wait_for_file() {
  local file="$1" attempt
  for attempt in {1..100}; do
    if docker exec "$container" test -e "$control_dir/$file"; then return 0; fi
    sleep 0.1
  done
  echo "Timed out waiting for $file" >&2
  return 1
}
wait_for_file ready

docker exec -d "$container" bash -c '
  PGAPPNAME=complex_ownership_race psql -X -v ON_ERROR_STOP=1 -U postgres -c "SET ROLE service_role; SELECT public.save_complex_with_courts('\''99999999-9999-9999-9999-999999999999'\'', '\''aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'\'', '\''{\"name\":\"Stolen\",\"address\":\"Street\"}'\''::jsonb, '\''[]'\''::jsonb, '\''{}'\''::uuid[]);" >"$1/b.log" 2>&1
  echo $? >"$1/b.exit"
' bash "$control_dir"

locked=false
for attempt in {1..100}; do
  if [[ "$(docker exec "$container" psql -XAt -U postgres -c "SELECT count(*) FROM pg_stat_activity WHERE application_name = 'complex_ownership_race' AND wait_event_type = 'Lock' AND wait_event = 'transactionid'")" == 1 ]]; then
    locked=true
    break
  fi
  if docker exec "$container" test -e "$control_dir/b.exit"; then break; fi
  sleep 0.1
done
if [[ "$locked" != true ]]; then
  echo 'Session B did not reach a transaction-ID lock before A committed' >&2
  docker exec "$container" bash -c 'for file in "$1"/{a,b}.log; do printf "%s: " "$file"; tail -n 5 "$file"; done' bash "$control_dir" >&2
  docker exec "$container" touch "$control_dir/release"
  exit 1
fi
echo 'Session B transaction-ID Lock observed before session A commit'
docker exec "$container" touch "$control_dir/release"
wait_for_file a.exit
wait_for_file b.exit
docker exec "$container" test "$(docker exec "$container" bash -c 'read -r code < "$1/a.exit"; echo "$code"' bash "$control_dir")" = 0
if ! docker exec "$container" grep -q 'complex belongs to another organization' "$control_dir/b.log"; then
  echo 'Concurrent ownership rejection missing (unsafe mutation detected)' >&2
  docker exec "$container" tail -n 8 "$control_dir/b.log" >&2
  exit 1
fi
docker exec "$container" psql -XAt -U postgres -c "SELECT organization_id::text || ':' || name FROM public.complexes WHERE id='99999999-9999-9999-9999-999999999999'" | grep -Fx 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb:Concurrent Beta'
echo 'Concurrent parent ownership rejection and original owner/name: passed'
