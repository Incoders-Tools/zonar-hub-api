# Production migration delivery

Migrations in `supabase/migrations` reach the current production Supabase
project only through `.github/workflows/deploy-supabase-migrations.yml`, and
only automatically. This cannot prevent database owners or external
integrations from applying migrations by another route; the operator must
disable competing automation as described below.

CI runs on pull requests and pushes never touch a hosted database. `ci.yml`
checks migrations in disposable PostgreSQL 17 and runs the planner and workflow
tests without secrets. A subsequent, separate deployment run may connect to
the current Supabase project after a qualifying `main` push.

## How a deployment happens

1. Work lands on `dev` freely; `dev` is not protected and never deploys.
2. To release, open a pull request from `dev` to `main` in this repository and
   merge it once CI is green. No issue reference and no reviewer approval are
   required by this workflow.
3. The merge pushes to `main`, which runs `CI`. When that run completes, the
   deploy workflow starts from the `workflow_run` event. There is no manual
   dispatch and no input to type.
4. `verify-promotion` (no secrets, no environment) checks that the CI run is a
   green `push` run on `main` in this repository, that `main` still points at
   the CI head SHA, and that this commit is the merge commit of exactly one
   merged same-repository `dev` to `main` pull request. This is only a trusted
   promotion when `main` protection also prevents direct pushes and bypasses.
   PR-only, failed and unfinished CI runs skip the gate; invalid or ambiguous
   qualifying runs fail it. Neither route reaches deployment.
5. `deploy` runs only when the gate succeeded. It enters the
   `supabase-production` environment, checks out the exact CI head SHA without
   persisted credentials and applies only the migrations still pending on the
   current project.

When remote history already contains the full local manifest, the plan reports
`pending=false` and skips `db push` and the post-check. The run still reads the
remote database and can fail on configuration, connectivity or history errors.
A promotion without new migration files may also apply migrations left pending
by an earlier failed run.

## Deploy job steps

| Step | Database secret | What it does |
|------|-----------------|--------------|
| Re-bind run to current main head after gate | no | `check-main-head`: CI head SHA from the event file equals the checkout and the current `main` head |
| Validate environment configuration | yes | rejects a linked project (`supabase/.temp`); `check-auto-config` validates `SUPABASE_PROJECT_REF`, `SUPABASE_APPROVED_BASELINE` and `SUPABASE_DB_URL` |
| Install verified Supabase CLI / version check | no | pinned 2.109.0 archive, SHA-256 checked before extraction |
| Read remote migration history | yes | `supabase migration list`; must exit 0 |
| Dry run pending migrations | yes | `supabase db push --dry-run`; must exit 0 |
| Derive automatic plan | no | `auto-plan` writes `pending` and `pending_count` to `GITHUB_OUTPUT` only after full validation |
| Re-bind run to current main head before applying | no | same check as above, only when `pending == 'true'` |
| Apply migrations | yes | `supabase db push --yes` exactly once, only when `pending == 'true'` |
| Verify remote migration history | yes | runs whenever the push step ran, including after a failed push, unless the run was cancelled; `verify-auto-applied` requires the remote history to equal the full local manifest |

The secret is mapped only into those five steps. Event data reaches scripts
through the event file, never by expression interpolation, and the GitHub token
only has `contents: read` (the gate also has `pull-requests: read`).

## Guarantees

Preflight failures stop before applying migrations. A connection or post-push
failure can occur after the database has been contacted; follow the failure
procedure below before retrying.

| Check | Enforced by |
|-------|-------------|
| Main is a green CI push of a merged same-repository `dev` to `main` PR | `check-ci-promotion` in `verify-promotion` |
| The checkout and current `main` head equal the CI head SHA, after the gate and again right before `db push` | `check-main-head` |
| `SUPABASE_PROJECT_REF` is a 20-character project ref and `SUPABASE_APPROVED_BASELINE` is a version | `check-auto-config` |
| `SUPABASE_DB_URL` targets `db.<ref>.supabase.co:5432`, one host, user `postgres` with a password, database `/postgres`, `sslmode` of `require`, `verify-ca` or `verify-full`, no host/port overrides in the query | `check-auto-config` |
| Supabase CLI is exactly 2.109.0 and its archive matches the pinned SHA-256 digest before extraction | "Install verified Supabase CLI" plus a version check |
| No other workflow pushes migrations, links a project or reads the database secret | `DeployWorkflowTests` |
| Local versions reported by the CLI equal the checked-out manifest | `auto-plan` |
| Remote history is non-empty, contains `SUPABASE_APPROVED_BASELINE`, and is a contiguous prefix of the manifest (no remote-only, diverging or skipped versions) | `auto-plan` |
| Pending migrations are exactly the manifest suffix missing remotely, and the dry run lists exactly those files in order; with nothing pending the dry run must be the exact "up to date" signal | `auto-plan` |
| After `db push --yes`, the remote history equals the complete local manifest | `verify-auto-applied` |
| The planner and workflow tests passed for the deployed commit | `migration-planner-tests` job in `ci.yml`, part of the green CI run the gate requires |

The workflow never uses `--include-all`, `migration repair`, `--linked`,
`supabase link` or seeding. It runs `db push --yes` at most once per run attempt.

CLI output can contain connection details, so it is written to runner temp files
and never printed or uploaded as an artifact. On a CLI failure the log shows a
generic "output withheld" error only; the files are discarded with the runner.
To diagnose, reproduce the command from a trusted workstation or against a
disposable database. The planner script never prints the database URL.

The workflow sets `SUPABASE_TELEMETRY_DISABLED=1` and `DO_NOT_TRACK=1` for
every job to opt out of CLI telemetry.

The manual subcommands of `verify_migration_plan.py` (`check-inputs`,
`check-context`, `check-db-url`, `check-plan`, `verify-applied`) are kept for
backward compatibility; no workflow references them.

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

## Operator setup (once, before the first merge to main)

These settings live in GitHub and Supabase, not in this repository. The
workflow cannot enforce them, and the automatic route is only safe with them.

1. **Protect `main` (mandatory).** Require a pull request before merging,
   require the CI status checks (including `migration-planner-tests`) and the
   `promotion-policy` check, block force pushes and deletions, and do not allow
   bypassing these rules. Without this, a fast-forward push of an open `dev`
   PR's head to `main` can be associated with that PR as merged and pass the
   gate.
2. **Restrict the environment (mandatory).** Configure `supabase-production`
   with **Deployment branches**: selected branches, `main` only. Verify that no
   required reviewers remain; adding one pauses deployment for approval. Apply
   any GitHub setting change only with its own authorization.
3. Add to that environment only:
   - Secret `SUPABASE_DB_URL`: the direct connection string, for example
     `postgresql://postgres:<percent-encoded-password>@db.<ref>.supabase.co:5432/postgres?sslmode=require`.
   - Variable `SUPABASE_PROJECT_REF`: the 20-character project ref.
   - Variable `SUPABASE_APPROVED_BASELINE`: see the next section.
   A missing or invalid value fails the run before the CLI is installed.
4. Disable every other path that can change the production schema:
   - In the Supabase dashboard, under the project's GitHub integration
     (Project Settings → Integrations → GitHub), disconnect the repository, or
     at least turn off automatic deployment of migrations to production and
     automatic branching. When enabled, Supabase applies `supabase/migrations`
     itself on merge to the production branch, outside this workflow.
   - Do not enable Supabase Branching for the production project while this
     workflow is the delivery path.
   - Do not store a Supabase personal access token (`SUPABASE_ACCESS_TOKEN`) in
     GitHub; the workflow tests reject **other** workflows that reference it or
     that run `supabase db push` or `supabase link`.
   - Recheck these settings whenever project owners change.
5. **Keep credentials environment-scoped (mandatory).** Do not define
   `SUPABASE_DB_URL`, `SUPABASE_PROJECT_REF` or `SUPABASE_APPROVED_BASELINE` at
   repository or organization level. GitHub falls
   back to those scopes when the environment value is missing, so a stray
   repository secret would bypass the environment boundary.
6. The direct host `db.<ref>.supabase.co` resolves to IPv6 unless the project has
   the IPv4 add-on. GitHub-hosted runners have no IPv6 egress, so enable the IPv4
   add-on (or use a self-hosted runner with IPv6) before the first merge to
   `main`; otherwise the history step fails and nothing is applied.

## Baseline bootstrap (once, outside the workflow)

The workflow reads remote history but refuses to apply migrations while its
table is empty. If production schema was created by hand, record the
already-applied migrations
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
only through the same reviewed process. Every migration after the baseline that
is on `main` is applied by the next green promotion.

## Concurrency and retry

Only deploy jobs that passed the gate join the `supabase-production-migrations`
concurrency group, with `cancel-in-progress: false`. A running deployment is
never cancelled. A newer gated run replaces a deploy job that is still queued;
the replaced run shows as cancelled, and the newer run applies everything
pending for the newer `main` head.

There is no manual retry input. To retry after fixing the cause (for example an
unreachable host), re-run the failed workflow run while its CI head is still the
`main` head; once `main` has moved, the re-bind checks reject it and the next
promotion deploys instead. If a previous run applied some migrations, the next
plan derives the shorter pending list from the remote history.

## Failure and roll-forward

Migrations are applied file by file, so a failure can leave earlier files of
the same run applied while later ones are not.

1. Read the "Verify remote migration history" step: it reports whether the
   remote history reached the full manifest when a push failed or hit its
   20-minute step timeout. For details, inspect the version-only history from a
   trusted workstation; runner CLI output is deliberately withheld.
2. If the run was cancelled, or the whole job timed out, that step does not
   run and the remote state is unknown. A killed CLI may leave a statement
   running on the server until the connection is dropped. Before the next
   promotion:
   - check for active sessions from the CLI with read-only access (for example
     `pg_stat_activity`) and wait for them to end;
   - read `supabase migration list --db-url "$SUPABASE_DB_URL"` from a trusted
     workstation, without printing the URL;
   - compare the remote history with the local manifest; a migration missing
     from the history may still have partially applied if it is not
     transactional.
   Do not cancel a run while "Apply migrations" is in progress.
3. Do not re-run blindly. Diagnose with a disposable database built from the
   same migrations, or with read-only access to production.
4. Roll forward: merge a new migration that fixes or reverts the change into
   `dev`, then promote `dev` to `main`; the next green CI run deploys it. Do not
   edit or delete migrations that are already in the remote history.
5. For data loss, restore from Supabase backups or point-in-time recovery,
   following the platform restore procedure, then re-establish the baseline.

## Residual risks

- Branch protection on `main`, the environment branch policy, secret scoping and
  the Supabase GitHub integration are platform settings; this repository cannot
  verify them.
- Every green promotion deploys without a human approval step; review happens
  on the `dev` to `main` pull request.
- Between the dry run and `db push`, another client could change the remote
  history. The post-push verification detects it but cannot prevent it.
- The pinned digest proves the archive is the one reviewed at pin time, not
  that the release itself is trustworthy.
- `actions/checkout@v4` is a first-party action referenced by tag.
- Telemetry opt-out relies on the CLI honouring `SUPABASE_TELEMETRY_DISABLED`
  and `DO_NOT_TRACK`; the workflow cannot verify it.

## Out of scope

Per-environment branches (for example a staging branch deploying to a staging
project) are explicitly out of scope. The workflow deploys only `main` to the
single project configured in `supabase-production`.

## Local verification

```bash
python -m unittest discover -s supabase/tests -p "test_*.py"
bash supabase/tests/run_complex_courts_atomic.sh
```
