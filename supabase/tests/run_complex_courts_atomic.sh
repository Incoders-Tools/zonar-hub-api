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
SQL

for migration in supabase/migrations/*.sql; do
  printf 'Applying %s\n' "$migration"
  docker exec -i "$container" psql -v ON_ERROR_STOP=1 -U postgres < "$migration"
done

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
  awk '/^CREATE FUNCTION public.save_complex_with_courts\(/ { copying=1; sub(/^CREATE FUNCTION/, "CREATE OR REPLACE FUNCTION") } copying { print } /^END \$\$;/ && copying { exit }' \
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
