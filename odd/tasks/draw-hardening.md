# Draw hardening (review follow-ups)

API repo, branch `fix/draw-hardening` from `origin/dev` (7c8a51d). Follow-ups from native review `review-74ed57fedebe4d73` (frontend `odd/tasks/tournament-draws.md` L18) and the flaky CI run of Incoders-Tools/zonar-hub-api#15. PR to `dev`, then promotion; the new migration goes to production only with the owner's confirmation at promotion time.

## Specs

- **S1 Close the round (user, L1).** "Ya esta activada supabase de zonar-hub asi podemos avanzar en todos los pendientes"
- **S2 Stale court id (review R3/R4, WARNING).** A stored `courtId` is re-validated when a result is edited, so a court later deactivated or removed from the tournament blocks editing that result (`ZonarHub.Application/Features/TournamentDraws/Results/RecordMatchResult.cs:65-72`).
- **S3 Orphan results (review R3, WARNING).** The SQL `results_exist` checks count non-`scheduled` result rows whose match no longer exists in the draw structure, which can block replacing or deleting a draft forever (`supabase/migrations/20261101100000_tournament_draw_revisions.sql:73-78`); the API already ignores such rows on read.
- **S4 Insert race untested (review R3, SUGGESTION).** The insert branch of `save_tournament_draw_draft` when a draw appeared concurrently has no SQL test (`:62-66`).
- **S5 Flaky harness.** `database-migrations` failed once right after a fresh `postgres:17` pull and passed on rerun: `supabase/tests/run_complex_courts_atomic.sh` polls `pg_isready`, which can succeed on the image's temporary init server before its restart.

### Design decisions

- **D1** Editing a result whose `courtId` equals the stored one keeps it without re-validation; a different `courtId` is validated as today; clearing keeps court and schedule as today.
- **D2** New additive migration `supabase/migrations/20261107100000_tournament_draw_orphan_results.sql`: `CREATE OR REPLACE` the affected functions so their "results exist" checks count only non-`scheduled` rows whose `match_id` exists in the stored `structure` (a match id under `phases[*].rounds[*].matches[*].id` or `phases[*].groups[*].rounds[*].matches[*].id`); same signatures, SECURITY INVOKER, `search_path` and grants. Never edit applied migrations.
- **D3** Harness waits for the final server: after `docker run`, wait for the image's "PostgreSQL init process complete; ready for start up." log line (bounded), then require `pg_isready` plus a successful `psql -c 'select 1'`; keep the failure message explicit.

## Tasks

| ID | Specs | Route | Commit |
| --- | --- | --- | --- |
| H1 Harness readiness (D3) | S5 | worker | |
| H2 Keep an unchanged stored court on result edits (D1) | S2 | worker | |
| H3 Orphan-aware results checks + SQL tests incl. insert race (D2) | S3, S4 | worker | |

## Log

- **L1** (user, 2026-11-07, verbatim): "Ya esta activada supabase de zonar-hub asi podemos avanzar en todos los pendientes"
- **L2** (evidence): production deploy run 42396861008 rerun applied 6 migrations ("Remote history equals the complete local manifest"); the earlier failure was the paused Supabase project, not the migrations.
