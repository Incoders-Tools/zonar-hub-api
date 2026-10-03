# Production migration delivery

The intended repository delivery path for migrations in `supabase/migrations` is
`.github/workflows/deploy-supabase-migrations.yml`. This cannot prevent database
owners or external integrations from applying migrations by another route; the
operator must disable competing automation as described below. Nothing in this
repository applies migrations automatically. Every database operation needs:

1. the change merged to `main`,
2. a manual `workflow_dispatch` from `main` with exact inputs, and
3. reviewer approval of the protected `supabase-production` environment.

Pull requests and pushes never touch a hosted database. `ci.yml` runs every
migration against a disposable PostgreSQL 17 container and has no secrets.

## Guarantees

The workflow stops before connecting if any check fails.

| Check | Enforced by |
|-------|-------------|
| Run is from `refs/heads/main` and `main_sha` is the current head | `check-context`, before approval, after approval, and again right before `db push` |
| `project_ref` input equals environment `SUPABASE_PROJECT_REF` | `check-db-url` |
| `SUPABASE_DB_URL` targets `db.<ref>.supabase.co:5432`, one host, user `postgres` with a password, database `/postgres`, `sslmode` of `require`, `verify-ca` or `verify-full`, no host/port overrides in the query | `check-db-url` |
| Supabase CLI is exactly 2.109.0 and its archive matches the pinned SHA-256 digest before extraction | "Install verified Supabase CLI" plus a version check |
| No other workflow pushes migrations, links a project or reads the database secret | `DeployWorkflowTests` in the preflight unit tests |
| Local versions reported by the CLI equal the checked-out manifest | `check-plan` |
| Remote history is non-empty, contains `SUPABASE_APPROVED_BASELINE`, and is a contiguous prefix of the manifest (no remote-only, diverging or skipped versions) | `check-plan` |
| Pending versions equal `expected_pending_versions` exactly, in order; an empty list stops the run | `check-plan` |
| `db push --dry-run` lists exactly those files | `check-plan` |
| After `db push --yes`, remote history ends at the last approved version | `verify-applied` |

The workflow never uses `--include-all`, `migration repair`, `--linked` or
`supabase link`. It runs `db push --yes` at most once per dispatch.

CLI output can contain connection details, so it is written to runner temp files
and never printed or uploaded as an artifact. On a CLI failure the log shows a
generic "output withheld" error only; the files are discarded with the runner.
To diagnose, reproduce the command from a trusted workstation or against a
disposable database. The preflight script never prints the database URL.

The workflow sets `SUPABASE_TELEMETRY_DISABLED=1` and `DO_NOT_TRACK=1` for
every job to opt out of CLI telemetry.

### Supabase CLI supply chain

The workflow does not use `supabase/setup-cli`: that action downloads the CLI
release archive without verifying a checksum, so pinning the action to a commit
would still trust a mutable release asset. Instead the deploy job downloads
`supabase_2.109.0_linux_amd64.tar.gz` over HTTPS and runs
`sha256sum --check --strict` against a digest pinned in the workflow before
extracting it. The pinned value
`d96c1ca0ef1f89582f6f001c306547d709644d35fa298c08600323d4eb9fdcf2` matches both
the official v2.109.0 `checksums.txt` and the GitHub release asset digest.

To upgrade the CLI, change `SUPABASE_CLI_VERSION` and `SUPABASE_CLI_SHA256`
together in one reviewed pull request, taking the digest from the new release's
`checksums.txt` and cross-checking it with the asset digest on the release page.
Update the expected version in "Verify Supabase CLI version" and the
`DeployWorkflowTests` constants in the same change.

## Operator setup (once)

These settings live in GitHub, not in this repository. The workflow cannot
enforce them, which is why manual dispatch is also required.

1. Protect `main`: require pull requests and passing CI, and block force pushes.
2. Create the environment `supabase-production` with:
   - **Required reviewers**: at least one person other than the dispatcher; enable
     "Prevent self-review".
   - **Deployment branches**: selected branches, `main` only.
3. Add to that environment only:
   - Secret `SUPABASE_DB_URL`: the direct connection string, for example
     `postgresql://postgres:<percent-encoded-password>@db.<ref>.supabase.co:5432/postgres?sslmode=require`.
   - Variable `SUPABASE_PROJECT_REF`: the 20-character project ref.
   - Variable `SUPABASE_APPROVED_BASELINE`: see the next section.
4. Disable every other path that can change the production schema, so a merge to
   `main` cannot bypass the approval gate:
   - In the Supabase dashboard, under the project's GitHub integration
     (Project Settings → Integrations → GitHub), disconnect the repository, or
     at least turn off automatic deployment of migrations to production and
     automatic branching. When enabled, Supabase applies `supabase/migrations`
     itself on merge to the production branch, outside this workflow.
   - Do not enable Supabase Branching for the production project while this
     workflow is the delivery path.
   - Do not store a Supabase personal access token (`SUPABASE_ACCESS_TOKEN`) in
     GitHub; the preflight tests reject workflows that reference it or that run
     `supabase db push` or `supabase link`.
   - Recheck these settings whenever project owners change.
5. Do not define `SUPABASE_DB_URL`, `SUPABASE_PROJECT_REF` or
   `SUPABASE_APPROVED_BASELINE` at repository or organization level. GitHub falls
   back to those scopes when the environment value is missing, so a stray
   repository secret would bypass the environment boundary.
6. The direct host `db.<ref>.supabase.co` resolves to IPv6 unless the project has
   the IPv4 add-on. GitHub-hosted runners have no IPv6 egress, so enable the IPv4
   add-on (or use a self-hosted runner with IPv6) before the first run.

## Baseline bootstrap (once, outside the workflow)

The workflow refuses to run while the remote history table is empty. If
production schema was created by hand, record the already-applied migrations
once, with a reviewed and recorded change:

1. Confirm that production schema matches the migrations up to the chosen
   baseline version (for example by comparing a `supabase db dump --schema-only`
   with a disposable database built from those migrations).
2. From a trusted workstation, mark exactly those versions as applied:
   `supabase migration repair --status applied <version> ... --db-url "$SUPABASE_DB_URL"`.
3. Check `supabase migration list --db-url "$SUPABASE_DB_URL"` shows a
   contiguous remote prefix ending at the baseline.
4. Set `SUPABASE_APPROVED_BASELINE` to that last version.

Never run `migration repair` from the workflow. Update the baseline variable
only through the same reviewed process.

## Deploying

1. Merge the migration to `main` and wait for CI.
2. List pending versions locally: they are the migration filenames after the
   last remote version, in filename order.
3. Run **Deploy Supabase migrations** from `main` with:
   - `main_sha`: full SHA of the current `main` head.
   - `project_ref`: the production project ref.
   - `expected_pending_versions`: exact list, for example
     `20261001010000,20261002100000`.
4. The reviewer checks the preflight job output and the inputs, then approves.
5. The deploy job prints the validated plan before applying it.

## Retry and concurrency

All runs share one static concurrency group with `cancel-in-progress: false`.
A running deployment is never cancelled, but a newer dispatch replaces a run that
is still queued; the replaced run shows as cancelled.

To retry, dispatch again with the current `main` head SHA and the current pending
list. Re-running an old run, or dispatching with an old SHA after `main` moved,
fails at `check-context` before the database is touched. If a previous run
applied some migrations, the pending list shrinks; the dry-run check rejects the
old list.

## Failure and rollback

Migrations are applied file by file, so a failure can leave earlier files of
the same run applied while later ones are not.

1. Read the "Verify remote migration history" step: it reports whether the
   approved history was reached when a push failed or hit its 20-minute step
   timeout. For details, inspect the version-only history from a trusted
   workstation; runner CLI output is deliberately withheld.
2. If the run was cancelled, or the whole job timed out, that step does not
   run and the remote state is unknown. A killed CLI may leave a statement
   running on the server until the connection is dropped. Before any new
   dispatch:
   - check for active sessions from the CLI with read-only access (for example
     `pg_stat_activity`) and wait for them to end;
   - read `supabase migration list --db-url "$SUPABASE_DB_URL"` from a trusted
     workstation, without printing the URL;
   - compare the remote history with the approved list; a migration missing
     from the history may still have partially applied if it is not
     transactional.
   Do not cancel a run while "Apply migrations" is in progress.
3. Do not rerun the same inputs blindly. Diagnose with a disposable database
   built from the same migrations, or with read-only access to production.
4. Roll forward: merge a new migration that fixes or reverts the change, then
   deploy it with this workflow. Do not edit or delete migrations that are
   already in the remote history.
5. For data loss, restore from Supabase backups or point-in-time recovery,
   following the platform restore procedure, then re-establish the baseline.

## Residual risks

- Reviewers, deployment branch rules, secret scoping and the Supabase GitHub
  integration are platform settings; this repository cannot verify them.
- Between the dry run and `db push`, another client could change the remote
  history. The post-push verification detects it but cannot prevent it.
- The pinned digest proves the archive is the one reviewed at pin time, not
  that the release itself is trustworthy.
- `actions/checkout@v4` is a first-party action referenced by tag.
- Telemetry opt-out relies on the CLI honouring `SUPABASE_TELEMETRY_DISABLED`
  and `DO_NOT_TRACK`; the workflow cannot verify it.

## Local verification

```bash
python -m unittest discover -s supabase/tests -p "test_*.py"
bash supabase/tests/run_complex_courts_atomic.sh
```
