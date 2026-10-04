"""Behavior tests for the production migration preflight (stdlib unittest only).

Run with: python -m unittest discover -s supabase/tests -p "test_*.py"
"""

import contextlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import verify_migration_plan as vmp  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = REPO_ROOT / ".github" / "workflows"
DEPLOY_WORKFLOW = WORKFLOWS / "deploy-supabase-migrations.yml"
PR_WORKFLOW = WORKFLOWS / "pr-validation.yml"
# sha256 of supabase_2.109.0_linux_amd64.tar.gz, from the official v2.109.0 checksums.txt.
CLI_SHA256 = "d96c1ca0ef1f89582f6f001c306547d709644d35fa298c08600323d4eb9fdcf2"

REF = "abcdefghijklmnopqrst"
OTHER_REF = "tsrqponmlkjihgfedcba"
SHA = "0123456789abcdef0123456789abcdef01234567"
OTHER_SHA = "fedcba9876543210fedcba9876543210fedcba98"
# Sentinel password: no test output may ever contain it.
SECRET = "S3cr3t-Sentinel-Pa55"
GOOD_URL = f"postgresql://postgres:{SECRET}@db.{REF}.supabase.co:5432/postgres?sslmode=require"

FILES = [
    "20260430120000_create_organizations.sql",
    "20260501043000_create_tenants_and_users.sql",
    "20260515_create_impersonation_tables.sql",
    "20261001010000_save_complex_with_courts.sql",
    "20261002100000_list_admin_users_by_organization.sql",
]
VERSIONS = [name.split("_", 1)[0] for name in FILES]
BASELINE = "20260501043000"


def rows(local, remote):
    """Builds CLI-shaped `migration list --output-format json` text."""
    merged = []
    for index in range(max(len(local), len(remote))):
        merged.append({
            "local": local[index] if index < len(local) else "",
            "remote": remote[index] if index < len(remote) else "",
            "time": "",
        })
    return json.dumps({"migrations": merged, "message": "Migrations listed"})


def dry_run(files):
    lines = [
        "DRY RUN: migrations will *not* be pushed to the database.",
        "Connecting to remote database...",
        "Would push these migrations:",
    ]
    lines += [f" \u2022 {name}" for name in files]
    lines.append("A new version of Supabase CLI is available")
    return "\n".join(lines) + "\n"


class ExpectedVersionsTests(unittest.TestCase):
    def test_accepts_comma_and_whitespace_separated_versions(self):
        self.assertEqual(
            vmp.parse_expected_versions(" 20261001010000, 20261002100000\n"),
            ["20261001010000", "20261002100000"],
        )

    def test_empty_list_stops_the_run(self):
        for text in ("", "  ", " , "):
            with self.assertRaisesRegex(vmp.PlanError, "empty"):
                vmp.parse_expected_versions(text)

    def test_rejects_non_numeric_and_duplicate_versions(self):
        with self.assertRaisesRegex(vmp.PlanError, "digits"):
            vmp.parse_expected_versions("20261001010000;drop")
        with self.assertRaisesRegex(vmp.PlanError, "duplicate"):
            vmp.parse_expected_versions("20261001010000 20261001010000")


class ManifestTests(unittest.TestCase):
    def test_reads_versions_in_cli_filename_order(self):
        with tempfile.TemporaryDirectory() as folder:
            for name in reversed(FILES):
                Path(folder, name).write_text("select 1;\n", encoding="utf-8")
            manifest = vmp.read_manifest(folder)
        self.assertEqual([entry.filename for entry in manifest], FILES)
        self.assertEqual([entry.version for entry in manifest], VERSIONS)

    def test_rejects_malformed_or_duplicate_versions_and_empty_folder(self):
        cases = (
            (["create_without_version.sql"], "not a valid migration"),
            (["20260101_a.sql", "20260101_b.sql"], "duplicate"),
            ([], "no migrations"),
        )
        for names, message in cases:
            with self.subTest(names=names), tempfile.TemporaryDirectory() as folder:
                for name in names:
                    Path(folder, name).write_text("", encoding="utf-8")
                Path(folder, "README.txt").write_text("", encoding="utf-8")
                with self.assertRaisesRegex(vmp.PlanError, message):
                    vmp.read_manifest(folder)


class MigrationListTests(unittest.TestCase):
    def test_parses_cli_json(self):
        parsed = vmp.parse_migration_list(rows(VERSIONS, VERSIONS[:2]))
        self.assertEqual(parsed[0], vmp.HistoryRow(VERSIONS[0], VERSIONS[0]))
        self.assertEqual(parsed[-1], vmp.HistoryRow(VERSIONS[-1], ""))

    def test_rejects_unexpected_shapes(self):
        for text in ("not json", "[]", '{"migrations": {}}', '{"migrations": [1]}',
                     '{"migrations": [{"local": 1, "remote": ""}]}'):
            with self.subTest(text=text), self.assertRaises(vmp.PlanError):
                vmp.parse_migration_list(text)


class DryRunTests(unittest.TestCase):
    def test_extracts_only_listed_migration_files(self):
        self.assertEqual(vmp.parse_dry_run(dry_run(FILES[3:])), FILES[3:])

    def test_up_to_date_or_unrecognized_output_fails_closed(self):
        for text in ("Remote database is up to date.\n", "", dry_run([])):
            with self.subTest(text=text), self.assertRaises(vmp.PlanError):
                vmp.parse_dry_run(text)


class PlanTests(unittest.TestCase):
    def manifest(self):
        return [vmp.ManifestEntry(v, f) for v, f in zip(VERSIONS, FILES)]

    def check(self, remote, expected, files, local=None):
        return vmp.check_plan(
            self.manifest(),
            vmp.parse_migration_list(rows(VERSIONS if local is None else local, remote)),
            BASELINE,
            expected,
            files,
        )

    def test_accepts_exact_pending_tail(self):
        self.assertEqual(self.check(VERSIONS[:3], VERSIONS[3:], FILES[3:]), FILES[3:])

    def test_rejects_operator_list_that_differs_from_pending_tail(self):
        # Guards against silently applying more (or fewer) migrations than approved.
        for expected in (VERSIONS[4:], VERSIONS[2:], [VERSIONS[4], VERSIONS[3]]):
            with self.subTest(expected=expected), self.assertRaisesRegex(vmp.PlanError, "pending"):
                self.check(VERSIONS[:3], expected, FILES[3:])

    def test_rejects_dry_run_that_differs_from_expected(self):
        with self.assertRaisesRegex(vmp.PlanError, "dry run"):
            self.check(VERSIONS[:3], VERSIONS[3:], FILES[4:])

    def test_rejects_empty_remote_history_and_missing_baseline(self):
        with self.assertRaisesRegex(vmp.PlanError, "empty"):
            self.check([], VERSIONS, FILES)
        with self.assertRaisesRegex(vmp.PlanError, "baseline"):
            self.check(VERSIONS[:1], VERSIONS[1:], FILES[1:])

    def test_rejects_baseline_absent_from_manifest(self):
        with self.assertRaisesRegex(vmp.PlanError, "baseline"):
            vmp.check_plan(self.manifest(), vmp.parse_migration_list(rows(VERSIONS, VERSIONS[:3])),
                           "20990101000000", VERSIONS[3:], FILES[3:])

    def test_rejects_cli_local_list_differing_from_manifest(self):
        with self.assertRaisesRegex(vmp.PlanError, "manifest"):
            self.check(VERSIONS[:3], VERSIONS[3:], FILES[3:], local=VERSIONS[:-1])

    def test_rejects_remote_only_overlap_and_gaps(self):
        cases = (
            # Remote-only version beyond the manifest.
            (VERSIONS + ["20990101000000"], VERSIONS, "remote-only"),
            # Row whose local and remote versions diverge.
            ([VERSIONS[0], VERSIONS[1], "20260510000000"], VERSIONS, "diverge"),
        )
        for remote, local, message in cases:
            with self.subTest(remote=remote), self.assertRaisesRegex(vmp.PlanError, message):
                self.check(remote, VERSIONS[3:], FILES[3:], local=local)
        gap = json.dumps({"migrations": [
            {"local": VERSIONS[0], "remote": VERSIONS[0]},
            {"local": VERSIONS[1], "remote": ""},
            {"local": VERSIONS[2], "remote": VERSIONS[2]},
            {"local": VERSIONS[3], "remote": ""},
            {"local": VERSIONS[4], "remote": ""},
        ]})
        with self.assertRaisesRegex(vmp.PlanError, "contiguous"):
            vmp.check_plan(self.manifest(), vmp.parse_migration_list(gap), VERSIONS[0],
                           VERSIONS[3:], FILES[3:])

    def test_verify_applied_requires_prefix_ending_at_last_expected(self):
        manifest = self.manifest()
        vmp.verify_applied(manifest, vmp.parse_migration_list(rows(VERSIONS, VERSIONS)),
                           BASELINE, VERSIONS[3:])
        for remote in (VERSIONS[:4], VERSIONS[:3]):
            with self.subTest(remote=remote), self.assertRaisesRegex(vmp.PlanError, "applied"):
                vmp.verify_applied(manifest, vmp.parse_migration_list(rows(VERSIONS, remote)),
                                   BASELINE, VERSIONS[3:])
        partial = VERSIONS[:3]
        with self.assertRaisesRegex(vmp.PlanError, "applied"):
            vmp.verify_applied(manifest, vmp.parse_migration_list(rows(VERSIONS, partial + VERSIONS[4:5])),
                               BASELINE, VERSIONS[3:])


def up_to_date():
    """Supabase CLI 2.109.0 `db push --dry-run` no-op output, stdout followed by stderr.

    Observed against disposable PostgreSQL 17: the up-to-date line is written to
    stdout while the DRY RUN banner and any pending list go to stderr, so callers
    must pass both streams.
    """
    return ("Remote database is up to date.\n"
            "WARN: config section [inbucket] is deprecated. Please use [local_smtp] instead.\n"
            "DRY RUN: migrations will *not* be pushed to the database.\n"
            "Connecting to remote database...\n"
            "A new version of Supabase CLI is available: v2.119.0 (currently installed v2.109.0)\n"
            "We recommend updating regularly for new features and bug fixes: "
            "https://supabase.com/docs/guides/cli/getting-started#updating-the-supabase-cli\n")


class AutoPlanTests(unittest.TestCase):
    """Pending migrations are derived from CLI state instead of an operator list."""

    def manifest(self):
        return [vmp.ManifestEntry(v, f) for v, f in zip(VERSIONS, FILES)]

    def history(self, remote, local=None):
        return vmp.parse_migration_list(rows(VERSIONS if local is None else local, remote))

    def test_derive_pending_returns_exact_manifest_suffix(self):
        for applied in (2, 3, 4):
            with self.subTest(applied=applied):
                pending = vmp.derive_pending(self.manifest(), self.history(VERSIONS[:applied]), BASELINE)
                self.assertEqual(pending, self.manifest()[applied:])

    def test_derive_pending_is_empty_when_remote_is_complete(self):
        self.assertEqual(vmp.derive_pending(self.manifest(), self.history(VERSIONS), BASELINE), [])

    def test_derive_pending_rejects_untrustworthy_history(self):
        gap = json.dumps({"migrations": [
            {"local": VERSIONS[0], "remote": VERSIONS[0]},
            {"local": VERSIONS[1], "remote": VERSIONS[1]},
            {"local": VERSIONS[2], "remote": ""},
            {"local": VERSIONS[3], "remote": VERSIONS[3]},
            {"local": VERSIONS[4], "remote": ""},
        ]})
        blank_row = json.dumps({"migrations": json.loads(rows(VERSIONS, VERSIONS[:3]))["migrations"]
                                + [{"local": "", "remote": ""}]})
        cases = (
            (self.history(VERSIONS + ["20990101000000"], VERSIONS), BASELINE, "remote-only"),
            (vmp.parse_migration_list(gap), BASELINE, "contiguous"),
            (self.history([]), BASELINE, "empty"),
            (self.history(VERSIONS[:1]), BASELINE, "baseline"),
            (self.history(VERSIONS[:3]), "20990101000000", "baseline"),
            (self.history(VERSIONS[:3]), "", "baseline"),
            (self.history([VERSIONS[1], VERSIONS[0], VERSIONS[2]]), BASELINE, "diverge"),
            (self.history(VERSIONS[:3], [VERSIONS[1], VERSIONS[0]] + VERSIONS[2:]), BASELINE, "manifest"),
            (self.history(VERSIONS[:3], VERSIONS[:-1]), BASELINE, "manifest"),
            (self.history(VERSIONS[:3], VERSIONS + [VERSIONS[0]]), BASELINE, "manifest"),
            (vmp.parse_migration_list(blank_row), BASELINE, "malformed"),
        )
        for history, baseline, message in cases:
            with self.subTest(message=message, baseline=baseline), \
                    self.assertRaisesRegex(vmp.PlanError, message):
                vmp.derive_pending(self.manifest(), history, baseline)

    def test_check_auto_plan_requires_dry_run_with_exact_ordered_pending_files(self):
        self.assertEqual(
            vmp.check_auto_plan(self.manifest(), self.history(VERSIONS[:3]), BASELINE, dry_run(FILES[3:])),
            FILES[3:],
        )
        mismatches = (
            dry_run(FILES[4:]),                              # fewer files than pending
            dry_run(FILES[2:]),                              # extra file already applied
            dry_run([FILES[4], FILES[3]]),                   # reordered
            dry_run(FILES[3:] + ["20990101000000_x.sql"]),   # file outside the manifest
        )
        for text in mismatches:
            with self.subTest(text=text), self.assertRaisesRegex(vmp.PlanError, "dry run"):
                vmp.check_auto_plan(self.manifest(), self.history(VERSIONS[:3]), BASELINE, text)

    def test_check_auto_plan_rejects_output_that_hides_pending_migrations(self):
        for text in (None, "", up_to_date(), dry_run([]), "Would push these migrations:\n",
                     dry_run(FILES[3:]) + "Remote database is up to date.\n"):
            with self.subTest(text=text), self.assertRaises(vmp.PlanError):
                vmp.check_auto_plan(self.manifest(), self.history(VERSIONS[:3]), BASELINE, text)

    def test_check_auto_plan_accepts_no_op_only_with_exact_up_to_date_signal(self):
        complete = self.history(VERSIONS)
        self.assertEqual(vmp.check_auto_plan(self.manifest(), complete, BASELINE, None), [])
        self.assertEqual(vmp.check_auto_plan(self.manifest(), complete, BASELINE, up_to_date()), [])
        stderr_only = up_to_date().split("\n", 1)[1]
        for text in ("", stderr_only, "Remote database is up to date\n",
                     "Remote database is not up to date.\n", dry_run(FILES[4:]),
                     up_to_date() + "Would push these migrations:\n",
                     up_to_date() + " \u2022 " + FILES[4] + "\n"):
            with self.subTest(text=text), self.assertRaisesRegex(vmp.PlanError, "up to date"):
                vmp.check_auto_plan(self.manifest(), complete, BASELINE, text)

    def test_check_auto_plan_rejects_up_to_date_signal_alongside_diagnostic_errors(self):
        # Benign CLI warnings stay accepted; any error diagnostic voids the no-op signal.
        complete = self.history(VERSIONS)
        diagnostics = (
            "ERROR: relation \"schema_migrations\" does not exist\n",
            "error: unexpected EOF\n",
            "FATAL: password authentication failed for user \"postgres\"\n",
            "failed to connect to postgres: failed to connect to `host=127.0.0.1 user=postgres "
            "database=postgres`: tls error (server refused TLS connection)\n",
            "Try rerunning the command with --debug to troubleshoot the error.\n",
            "panic: runtime error: invalid memory address\n",
        )
        for line in diagnostics:
            for text in (up_to_date() + line, line + up_to_date()):
                with self.subTest(text=text), self.assertRaisesRegex(vmp.PlanError, "up to date"):
                    vmp.check_auto_plan(self.manifest(), complete, BASELINE, text)

    def test_check_auto_plan_validates_history_before_trusting_dry_run(self):
        with self.assertRaisesRegex(vmp.PlanError, "remote-only"):
            vmp.check_auto_plan(self.manifest(), self.history(VERSIONS + ["20990101000000"], VERSIONS),
                                BASELINE, up_to_date())

    def test_verify_auto_applied_requires_complete_manifest_history(self):
        vmp.verify_auto_applied(self.manifest(), self.history(VERSIONS), BASELINE)
        partial_gap = json.dumps({"migrations": json.loads(rows(VERSIONS, VERSIONS[:3]))["migrations"][:3] + [
            {"local": VERSIONS[3], "remote": ""},
            {"local": VERSIONS[4], "remote": VERSIONS[4]},
        ]})
        cases = (
            self.history(VERSIONS[:4]),                               # partial apply
            self.history(VERSIONS[:3]),                               # nothing applied
            vmp.parse_migration_list(partial_gap),                    # out-of-order apply
            self.history(VERSIONS + ["20990101000000"], VERSIONS),    # extra remote version
            self.history([]),                                         # history vanished
        )
        for history in cases:
            with self.subTest(history=history), self.assertRaisesRegex(vmp.PlanError, "applied"):
                vmp.verify_auto_applied(self.manifest(), history, BASELINE)


class DatabaseUrlTests(unittest.TestCase):
    def test_accepts_direct_host_with_required_or_stronger_tls(self):
        for mode in ("require", "verify-ca", "verify-full"):
            url = GOOD_URL.replace("sslmode=require", f"sslmode={mode}")
            with self.subTest(mode=mode):
                vmp.validate_db_url(url, REF)
        vmp.validate_db_url(GOOD_URL.replace(":5432", ""), REF)

    def test_rejects_unsafe_urls_without_echoing_them(self):
        cases = {
            "missing": "",
            "scheme": GOOD_URL.replace("postgresql://", "mysql://"),
            "other project": GOOD_URL.replace(REF, OTHER_REF),
            "pooler": GOOD_URL.replace(f"db.{REF}.supabase.co:5432", "aws-0-us-east-1.pooler.supabase.com:6543"),
            "suffix trick": GOOD_URL.replace(".supabase.co", ".supabase.co.evil.example"),
            "port": GOOD_URL.replace(":5432", ":6543"),
            "no sslmode": GOOD_URL.replace("?sslmode=require", ""),
            "weak sslmode": GOOD_URL.replace("sslmode=require", "sslmode=prefer"),
            "duplicate sslmode": GOOD_URL + "&sslmode=disable",
            "host override": GOOD_URL + "&host=evil.example",
            "multi host": GOOD_URL.replace(":5432/", ":5432,evil.example:5432/"),
            "fragment": GOOD_URL + "#x",
            "pooler user": GOOD_URL.replace("postgres:", f"postgres.{REF}:", 1),
            "other user": GOOD_URL.replace("postgres:", "evil_admin:", 1),
            "no user": GOOD_URL.replace(f"postgres:{SECRET}@", ""),
            "no password": GOOD_URL.replace(f":{SECRET}", ""),
            "other database": GOOD_URL.replace("/postgres?", "/evil_db?"),
            "no database": GOOD_URL.replace("/postgres?", "?"),
            "nested path": GOOD_URL.replace("/postgres?", "/postgres/evil?"),
        }
        for label, url in cases.items():
            with self.subTest(label=label):
                with self.assertRaises(vmp.PlanError) as raised:
                    vmp.validate_db_url(url, REF)
                self.assertNotIn(SECRET, str(raised.exception))
                self.assertNotIn("evil", str(raised.exception))

    def test_rejects_invalid_project_ref(self):
        for ref in ("", "ABCDEFGHIJKLMNOPQRST", "short", REF + "x", "abc.defghijklmnopqrs"):
            with self.subTest(ref=ref), self.assertRaisesRegex(vmp.PlanError, "project ref"):
                vmp.validate_db_url(GOOD_URL, ref)


class ContextTests(unittest.TestCase):
    def test_accepts_dispatch_bound_to_current_main_head(self):
        vmp.validate_context("refs/heads/main", SHA, SHA, SHA, SHA)

    def test_rejects_other_refs_stale_or_malformed_shas(self):
        cases = (
            ("refs/heads/dev", SHA, SHA, SHA, SHA, "refs/heads/main"),
            ("refs/heads/main", "abc", SHA, SHA, SHA, "40"),
            ("refs/heads/main", SHA.upper(), SHA, SHA, SHA, "40"),
            ("refs/heads/main", SHA, OTHER_SHA, SHA, SHA, "dispatched"),
            ("refs/heads/main", SHA, SHA, OTHER_SHA, SHA, "checked out"),
            ("refs/heads/main", SHA, SHA, SHA, OTHER_SHA, "stale"),
        )
        for *args, message in cases:
            with self.subTest(args=args), self.assertRaisesRegex(vmp.PlanError, message):
                vmp.validate_context(*args)


CI_REPO = "incoders/zonar-hub-api"
FORK = "attacker/zonar-hub-api"


def ci_run(**overrides):
    """A GitHub `workflow_run` object for a successful CI push run on main."""
    run = {
        "id": 101,
        "name": "CI",
        "path": ".github/workflows/ci.yml",
        "event": "push",
        "status": "completed",
        "conclusion": "success",
        "head_branch": "main",
        "head_sha": SHA,
        "repository": {"full_name": CI_REPO},
        "head_repository": {"full_name": CI_REPO},
        "pull_requests": [],
    }
    run.update(overrides)
    return run


def merged_pr(number=7, **overrides):
    """A pull request object as listed for a commit, merged from dev into main."""
    pr = {
        "number": number,
        "state": "closed",
        "merged_at": "2026-10-03T22:00:00Z",
        "merge_commit_sha": SHA,
        "base": {"ref": "main", "repo": {"full_name": CI_REPO}},
        "head": {"ref": "dev", "repo": {"full_name": CI_REPO}},
    }
    pr.update(overrides)
    return pr


class CiPromotionTests(unittest.TestCase):
    """Pure validation of the CI run and promotion PR that authorize a main commit."""

    def validate(self, run=None, pulls=None, repo=CI_REPO, checkout=SHA, remote=SHA):
        return vmp.validate_ci_promotion(ci_run() if run is None else run,
                                         [merged_pr()] if pulls is None else pulls,
                                         repo, checkout, remote)

    def test_accepts_successful_main_push_ci_for_merged_dev_promotion(self):
        self.assertEqual(self.validate(), SHA)

    def test_ignores_open_or_unmerged_prs_that_only_contain_the_commit(self):
        others = [
            merged_pr(8, state="open", merged_at=None, merge_commit_sha=OTHER_SHA,
                      head={"ref": "feature/x", "repo": {"full_name": FORK}}),
            merged_pr(9, state="closed", merged_at=None, merge_commit_sha=SHA),
        ]
        self.assertEqual(self.validate(pulls=others + [merged_pr()]), SHA)

    def test_rejects_runs_that_are_not_a_completed_successful_main_push(self):
        cases = (
            ({"conclusion": "failure"}, "successful"),
            ({"conclusion": "cancelled"}, "successful"),
            ({"conclusion": None}, "successful"),
            ({"status": "in_progress", "conclusion": None}, "completed"),
            ({"status": "queued"}, "completed"),
            ({"event": "pull_request"}, "push"),
            ({"event": "pull_request_target"}, "push"),
            ({"event": "workflow_dispatch"}, "push"),
            ({"head_branch": "dev"}, "main"),
            ({"head_branch": "refs/heads/main"}, "main"),
            ({"path": ".github/workflows/pr-validation.yml"}, "CI workflow"),
            ({"path": ".github/workflows/ci.yml@refs/heads/dev"}, "CI workflow"),
            ({"repository": {"full_name": FORK}}, "repository"),
            ({"head_repository": {"full_name": FORK}}, "repository"),
            ({"head_repository": None}, "repository"),
        )
        for overrides, message in cases:
            with self.subTest(overrides=overrides), self.assertRaisesRegex(vmp.PlanError, message):
                self.validate(run=ci_run(**overrides))

    def test_rejects_malformed_run_payloads(self):
        missing_sha = ci_run()
        del missing_sha["head_sha"]
        cases = (None, [], "run", missing_sha, ci_run(head_sha=SHA.upper()), ci_run(head_sha=SHA[:7]),
                 ci_run(head_sha=1), ci_run(repository="incoders/zonar-hub-api"),
                 ci_run(repository={"full_name": None}), ci_run(status=["completed"]))
        for run in cases:
            with self.subTest(run=run), self.assertRaises(vmp.PlanError):
                vmp.validate_ci_promotion(run, [merged_pr()], CI_REPO, SHA, SHA)

    def test_rejects_stale_or_mismatched_commit_shas(self):
        cases = (
            ({"run": ci_run(head_sha=OTHER_SHA)}, "checked out"),
            ({"checkout": OTHER_SHA}, "checked out"),
            ({"checkout": ""}, "checked out"),
            ({"remote": OTHER_SHA}, "stale"),
            ({"remote": ""}, "stale"),
        )
        for kwargs, message in cases:
            with self.subTest(kwargs=kwargs), self.assertRaisesRegex(vmp.PlanError, message):
                self.validate(**kwargs)

    def test_rejects_direct_push_without_merged_promotion_pr(self):
        unmerged = merged_pr(merged_at=None)
        for pulls in ([], [unmerged], [merged_pr(merge_commit_sha=OTHER_SHA)]):
            with self.subTest(pulls=pulls), self.assertRaisesRegex(vmp.PlanError, "no merged pull request"):
                self.validate(pulls=pulls)

    def test_rejects_promotion_pr_from_wrong_branch_or_repository(self):
        cases = (
            {"head": {"ref": "feature/x", "repo": {"full_name": CI_REPO}}},
            {"head": {"ref": "Dev", "repo": {"full_name": CI_REPO}}},
            {"head": {"ref": "dev", "repo": {"full_name": FORK}}},
            {"head": {"ref": "dev", "repo": None}},
            {"base": {"ref": "dev", "repo": {"full_name": CI_REPO}}},
            {"base": {"ref": "main", "repo": {"full_name": FORK}}},
        )
        for overrides in cases:
            with self.subTest(overrides=overrides), self.assertRaisesRegex(vmp.PlanError, "dev"):
                self.validate(pulls=[merged_pr(**overrides)])

    def test_rejects_ambiguous_promotion_prs(self):
        cases = (
            [merged_pr(7), merged_pr(8)],
            [merged_pr(7), merged_pr(8, head={"ref": "feature/x", "repo": {"full_name": CI_REPO}})],
        )
        for pulls in cases:
            with self.subTest(pulls=pulls), self.assertRaisesRegex(vmp.PlanError, "more than one"):
                self.validate(pulls=pulls)

    def test_rejects_malformed_pull_request_lists(self):
        no_head = merged_pr()
        del no_head["head"]
        cases = (None, {}, "pulls", [None], ["pr"], [no_head], [merged_pr(merged_at=1)],
                 [merged_pr(merged_at="")], [merged_pr(merged_at="  ")],
                 [merged_pr(merge_commit_sha=None)], [merged_pr(base="main")],
                 [merged_pr(head={"ref": ["dev"], "repo": {"full_name": CI_REPO}})],
                 [merged_pr(), None])
        for pulls in cases:
            with self.subTest(pulls=pulls), self.assertRaises(vmp.PlanError):
                vmp.validate_ci_promotion(ci_run(), pulls, CI_REPO, SHA, SHA)

    def test_rejects_invalid_expected_repository(self):
        for repo in ("", "zonar-hub-api", "incoders/zonar-hub-api/extra", " incoders/zonar-hub-api", None):
            with self.subTest(repo=repo), self.assertRaisesRegex(vmp.PlanError, "repository"):
                self.validate(repo=repo)
        with self.assertRaisesRegex(vmp.PlanError, "repository"):
            self.validate(repo=FORK)

    def test_error_messages_never_echo_hostile_payload_values(self):
        hostile = "main\n::stop-commands::tok`touch pwned`"
        cases = (
            {"run": ci_run(head_branch=hostile)},
            {"run": ci_run(event=hostile)},
            {"run": ci_run(repository={"full_name": hostile})},
            {"pulls": [merged_pr(head={"ref": hostile, "repo": {"full_name": CI_REPO}})]},
        )
        for kwargs in cases:
            with self.subTest(kwargs=kwargs):
                with self.assertRaises(vmp.PlanError) as raised:
                    self.validate(**kwargs)
                self.assertNotIn("stop-commands", str(raised.exception))
                self.assertNotIn("pwned", str(raised.exception))

    def test_does_not_mutate_inputs(self):
        run, pulls = ci_run(), [merged_pr()]
        snapshot = json.dumps([run, pulls], sort_keys=True)
        self.validate(run=run, pulls=pulls)
        self.assertEqual(json.dumps([run, pulls], sort_keys=True), snapshot)


class CommandLineTests(unittest.TestCase):
    def run_main(self, argv, env):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = vmp.main(argv, env)
        return code, out.getvalue() + err.getvalue()

    def write(self, folder, name, text):
        path = Path(folder, name)
        path.write_text(text, encoding="utf-8")
        return str(path)

    def migrations_dir(self, folder):
        target = Path(folder, "migrations")
        target.mkdir()
        for name in FILES:
            Path(target, name).write_text("select 1;\n", encoding="utf-8")
        return str(target)

    def plan_env(self):
        return {
            "SUPABASE_APPROVED_BASELINE": BASELINE,
            "EXPECTED_PENDING_VERSIONS": ",".join(VERSIONS[3:]),
        }

    def test_check_db_url_reads_secret_from_environment_only(self):
        env = {"SUPABASE_DB_URL": GOOD_URL, "SUPABASE_PROJECT_REF": REF, "INPUT_PROJECT_REF": REF}
        code, output = self.run_main(["check-db-url"], env)
        self.assertEqual(code, 0)
        self.assertNotIn(SECRET, output)

    def test_check_db_url_failures_never_print_the_secret(self):
        bad_url = GOOD_URL.replace("sslmode=require", "sslmode=disable")
        cases = (
            {"SUPABASE_DB_URL": bad_url, "SUPABASE_PROJECT_REF": REF, "INPUT_PROJECT_REF": REF},
            {"SUPABASE_DB_URL": GOOD_URL, "SUPABASE_PROJECT_REF": REF, "INPUT_PROJECT_REF": OTHER_REF},
            {"SUPABASE_DB_URL": GOOD_URL, "INPUT_PROJECT_REF": REF},
        )
        for env in cases:
            with self.subTest(keys=sorted(env)):
                code, output = self.run_main(["check-db-url"], env)
                self.assertEqual(code, 1)
                self.assertIn("::error::", output)
                self.assertNotIn(SECRET, output)

    def test_check_context_uses_environment(self):
        env = {"GITHUB_REF": "refs/heads/main", "INPUT_MAIN_SHA": SHA, "GITHUB_SHA": SHA,
               "CHECKOUT_SHA": SHA, "REMOTE_MAIN_SHA": SHA}
        self.assertEqual(self.run_main(["check-context"], env)[0], 0)
        env["REMOTE_MAIN_SHA"] = OTHER_SHA
        code, output = self.run_main(["check-context"], env)
        self.assertEqual(code, 1)
        self.assertIn("stale", output)

    def test_check_inputs_validates_without_secrets(self):
        env = {"INPUT_PROJECT_REF": REF, "INPUT_MAIN_SHA": SHA,
               "EXPECTED_PENDING_VERSIONS": "20261001010000"}
        self.assertEqual(self.run_main(["check-inputs"], env)[0], 0)
        for key, value in (("EXPECTED_PENDING_VERSIONS", ""), ("INPUT_MAIN_SHA", "main"),
                           ("INPUT_PROJECT_REF", "evil.example/")):
            with self.subTest(key=key):
                code, output = self.run_main(["check-inputs"], {**env, key: value})
                self.assertEqual(code, 1)
                self.assertIn("::error::", output)

    def test_check_plan_and_verify_applied_commands(self):
        with tempfile.TemporaryDirectory() as folder:
            migrations = self.migrations_dir(folder)
            before = self.write(folder, "before.json", rows(VERSIONS, VERSIONS[:3]))
            log = self.write(folder, "dry.log", dry_run(FILES[3:]))
            after = self.write(folder, "after.json", rows(VERSIONS, VERSIONS))
            code, output = self.run_main(
                ["check-plan", "--migrations-dir", migrations, "--list-json", before, "--dry-run-log", log],
                self.plan_env())
            self.assertEqual(code, 0, output)
            self.assertIn(FILES[3], output)
            code, output = self.run_main(
                ["verify-applied", "--migrations-dir", migrations, "--list-json", after], self.plan_env())
            self.assertEqual(code, 0, output)
            code, output = self.run_main(
                ["verify-applied", "--migrations-dir", migrations, "--list-json", before], self.plan_env())
            self.assertEqual(code, 1)

    def test_missing_files_baseline_and_unknown_commands_fail_closed(self):
        with tempfile.TemporaryDirectory() as folder:
            migrations = self.migrations_dir(folder)
            listing = self.write(folder, "list.json", rows(VERSIONS, VERSIONS[:3]))
            cases = (
                (["--migrations-dir", "missing", "--list-json", listing], self.plan_env(), "directory"),
                (["--migrations-dir", migrations, "--list-json", "missing.json"], self.plan_env(), "missing"),
                (["--migrations-dir", migrations, "--list-json", listing],
                 {**self.plan_env(), "SUPABASE_APPROVED_BASELINE": ""}, "BASELINE"),
            )
            for args, env, message in cases:
                with self.subTest(message=message):
                    code, output = self.run_main(["check-plan", *args, "--dry-run-log", "missing.log"], env)
                    self.assertEqual(code, 1)
                    self.assertIn(message, output)
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            vmp.main(["unknown"], {})

    def test_manual_commands_still_require_the_operator_approved_list(self):
        with tempfile.TemporaryDirectory() as folder:
            migrations = self.migrations_dir(folder)
            listing = self.write(folder, "list.json", rows(VERSIONS, VERSIONS))
            code, output = self.run_main(["verify-applied", "--migrations-dir", migrations,
                                          "--list-json", listing], {"SUPABASE_APPROVED_BASELINE": BASELINE})
        self.assertEqual(code, 1)
        self.assertIn("empty", output)

    def test_script_entry_point_exit_code_and_output_hide_the_secret(self):
        script = Path(vmp.__file__)
        env = {**os.environ, "SUPABASE_DB_URL": GOOD_URL.replace("sslmode=require", "sslmode=allow"),
               "SUPABASE_PROJECT_REF": REF, "INPUT_PROJECT_REF": REF}
        completed = subprocess.run([sys.executable, str(script), "check-db-url"], env=env,
                                   capture_output=True, text=True, check=False)
        self.assertEqual(completed.returncode, 1)
        self.assertIn("sslmode", completed.stderr)
        self.assertNotIn(SECRET, completed.stdout + completed.stderr)


# Hostile text planted in CLI and GitHub payloads; no command output may echo it.
HOSTILE = "::stop-commands::tok`touch pwned`"


class AutoCommandLineTests(unittest.TestCase):
    """CLI entry points for the automatic CI-gated delivery path."""

    def setUp(self):
        workdir = tempfile.TemporaryDirectory()
        self.addCleanup(workdir.cleanup)
        self.folder = workdir.name
        self.migrations = str(Path(self.folder, "migrations"))
        Path(self.migrations).mkdir()
        for name in FILES:
            Path(self.migrations, name).write_text("select 1;\n", encoding="utf-8")
        self.output = self.write("github_output", "")

    def write(self, name, text):
        path = Path(self.folder, name)
        path.write_text(text, encoding="utf-8")
        return str(path)

    def run_main(self, argv, env):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = vmp.main(argv, env)
        return code, out.getvalue() + err.getvalue()

    def outputs(self):
        return Path(self.output).read_text(encoding="utf-8")

    # check-ci-promotion -------------------------------------------------

    def promotion_env(self, **overrides):
        env = {"GITHUB_REPOSITORY": CI_REPO, "CHECKOUT_SHA": SHA, "REMOTE_MAIN_SHA": SHA}
        env.update(overrides)
        return env

    def promote(self, run=None, pulls=None, env=None, run_text=None, pulls_text=None):
        run_path = self.write("run.json", json.dumps(ci_run() if run is None else run)
                              if run_text is None else run_text)
        pulls_path = self.write("pulls.json", json.dumps([merged_pr()] if pulls is None else pulls)
                                if pulls_text is None else pulls_text)
        return self.run_main(["check-ci-promotion", "--run-json", run_path, "--pulls-json", pulls_path],
                             self.promotion_env() if env is None else env)

    def test_check_ci_promotion_accepts_green_dev_promotion_from_files(self):
        code, output = self.promote()
        self.assertEqual(code, 0, output)
        self.assertIn("CI promotion verified", output)
        self.assertEqual(self.outputs(), "", "promotion check must not write outputs")

    def test_check_ci_promotion_rejects_invalid_or_missing_json_with_fixed_messages(self):
        cases = (
            ({"run_text": "{not json " + HOSTILE}, "workflow run"),
            ({"run_text": ""}, "workflow run"),
            ({"pulls_text": "[" + HOSTILE}, "pull request"),
            ({"pulls_text": ""}, "pull request"),
        )
        for kwargs, message in cases:
            with self.subTest(kwargs=kwargs):
                code, output = self.promote(**kwargs)
                self.assertEqual(code, 1)
                self.assertIn("::error::", output)
                self.assertIn(message, output)
                self.assertIn("not valid JSON", output)
                self.assertNotIn("pwned", output)
        for missing in ("--run-json", "--pulls-json"):
            with self.subTest(missing=missing):
                argv = ["check-ci-promotion", "--run-json", self.write("run.json", json.dumps(ci_run())),
                        "--pulls-json", self.write("pulls.json", json.dumps([merged_pr()]))]
                argv[argv.index(missing) + 1] = str(Path(self.folder, "absent.json"))
                code, output = self.run_main(argv, self.promotion_env())
                self.assertEqual(code, 1)
                self.assertIn("missing or unreadable", output)

    def test_check_ci_promotion_binds_to_environment_repository_and_shas(self):
        cases = (
            (self.promotion_env(GITHUB_REPOSITORY=FORK), "repository"),
            ({"CHECKOUT_SHA": SHA, "REMOTE_MAIN_SHA": SHA}, "repository"),
            (self.promotion_env(CHECKOUT_SHA=OTHER_SHA), "checked out"),
            ({"GITHUB_REPOSITORY": CI_REPO, "REMOTE_MAIN_SHA": SHA}, "checked out"),
            (self.promotion_env(REMOTE_MAIN_SHA=OTHER_SHA), "stale"),
            ({"GITHUB_REPOSITORY": CI_REPO, "CHECKOUT_SHA": SHA}, "stale"),
        )
        for env, message in cases:
            with self.subTest(env=env):
                code, output = self.promote(env=env)
                self.assertEqual(code, 1)
                self.assertIn(message, output)

    def test_check_ci_promotion_rejects_untrusted_runs_without_echoing_payloads(self):
        cases = (
            {"run": ci_run(conclusion="failure", head_branch="main\n" + HOSTILE)},
            {"run": ci_run(event=HOSTILE)},
            {"run": [HOSTILE]},
            {"pulls": []},
            {"pulls": [merged_pr(head={"ref": HOSTILE, "repo": {"full_name": CI_REPO}})]},
            {"pulls": {"items": HOSTILE}},
        )
        for kwargs in cases:
            with self.subTest(kwargs=kwargs):
                code, output = self.promote(**kwargs)
                self.assertEqual(code, 1)
                self.assertIn("::error::", output)
                self.assertNotIn("pwned", output)
                self.assertNotIn("stop-commands", output)

    # auto-plan ------------------------------------------------------------

    def auto_env(self, **overrides):
        env = {"SUPABASE_APPROVED_BASELINE": BASELINE, "GITHUB_OUTPUT": self.output}
        env.update(overrides)
        return {key: value for key, value in env.items() if value is not None}

    def auto_plan(self, remote, log_text, env=None, listing=None):
        list_path = self.write("list.json", rows(VERSIONS, remote) if listing is None else listing)
        log_path = self.write("dry.log", log_text)
        return self.run_main(["auto-plan", "--migrations-dir", self.migrations, "--list-json", list_path,
                              "--dry-run-log", log_path], self.auto_env() if env is None else env)

    def test_auto_plan_writes_pending_outputs_after_full_validation(self):
        log = dry_run(FILES[3:]) + HOSTILE + "\n"
        code, output = self.auto_plan(VERSIONS[:3], log)
        self.assertEqual(code, 0, output)
        self.assertEqual(self.outputs(), "pending=true\npending_count=2\n")
        self.assertIn(FILES[3], output)
        for fragment in ("pwned", "Connecting", "DRY RUN", "Would push"):
            with self.subTest(fragment=fragment):
                self.assertNotIn(fragment, output)

    def test_auto_plan_no_op_succeeds_with_zero_pending(self):
        code, output = self.auto_plan(VERSIONS, up_to_date())
        self.assertEqual(code, 0, output)
        self.assertEqual(self.outputs(), "pending=false\npending_count=0\n")
        self.assertNotIn("Remote database is up to date", output)
        self.assertNotIn("WARN", output)

    def test_auto_plan_appends_after_existing_newline_terminated_outputs(self):
        Path(self.output).write_text("other=1\n", encoding="utf-8")
        code, output = self.auto_plan(VERSIONS[:4], dry_run(FILES[4:]))
        self.assertEqual(code, 0, output)
        self.assertEqual(self.outputs(), "other=1\npending=true\npending_count=1\n")

    def test_auto_plan_failed_preflight_writes_no_output(self):
        gap = json.dumps({"migrations": [
            {"local": VERSIONS[0], "remote": VERSIONS[0]},
            {"local": VERSIONS[1], "remote": VERSIONS[1]},
            {"local": VERSIONS[2], "remote": ""},
            {"local": VERSIONS[3], "remote": VERSIONS[3]},
            {"local": VERSIONS[4], "remote": ""},
        ]})
        cases = (
            (VERSIONS[:3], dry_run(FILES[4:]), None, "dry run"),
            (VERSIONS[:3], up_to_date(), None, "up to date"),
            (VERSIONS, up_to_date() + "FATAL: " + HOSTILE + "\n", None, "up to date"),
            (VERSIONS, dry_run(FILES[4:]), None, "up to date"),
            (VERSIONS[:3], dry_run(FILES[3:]), "not json " + HOSTILE, "not valid JSON"),
            (VERSIONS[:3], dry_run(FILES[3:]), json.dumps({"migrations": [{"local": HOSTILE, "remote": 1}]}),
             "unexpected shape"),
            (VERSIONS[:3], dry_run(FILES[3:]), gap, "contiguous"),
            (VERSIONS + ["20990101000000"], up_to_date(), rows(VERSIONS, VERSIONS + ["20990101000000"]),
             "remote-only"),
        )
        for remote, log, listing, message in cases:
            with self.subTest(message=message, log=log[:30]):
                code, output = self.auto_plan(remote, log, listing=listing)
                self.assertEqual(code, 1)
                self.assertIn(message, output)
                self.assertNotIn("pwned", output)
                self.assertEqual(self.outputs(), "", "no output may be written on failure")

    def test_auto_plan_rejects_missing_or_invalid_baseline_and_inputs_without_output(self):
        cases = (
            (self.auto_env(SUPABASE_APPROVED_BASELINE=None), "BASELINE"),
            (self.auto_env(SUPABASE_APPROVED_BASELINE="2026; drop"), "BASELINE"),
            (self.auto_env(SUPABASE_APPROVED_BASELINE="20990101000000"), "baseline"),
        )
        for env, message in cases:
            with self.subTest(env=env):
                code, output = self.auto_plan(VERSIONS[:3], dry_run(FILES[3:]), env=env)
                self.assertEqual(code, 1)
                self.assertIn(message, output)
                self.assertEqual(self.outputs(), "")
        for flag in ("--migrations-dir", "--list-json", "--dry-run-log"):
            with self.subTest(flag=flag):
                argv = ["auto-plan", "--migrations-dir", self.migrations,
                        "--list-json", self.write("list.json", rows(VERSIONS, VERSIONS[:3])),
                        "--dry-run-log", self.write("dry.log", dry_run(FILES[3:]))]
                argv[argv.index(flag) + 1] = str(Path(self.folder, "absent"))
                code, output = self.run_main(argv, self.auto_env())
                self.assertEqual(code, 1)
                self.assertIn("::error::", output)
                self.assertEqual(self.outputs(), "")

    def test_auto_plan_requires_trusted_github_output_file(self):
        directory = Path(self.folder, "output_dir")
        directory.mkdir()
        for value in (None, "", "relative/github_output", str(Path(self.folder, "absent_output")),
                      str(directory), self.output + "\npending=true", self.output + "\r",
                      self.output + "\x00", self.output + "\t"):
            with self.subTest(value=value):
                code, output = self.auto_plan(VERSIONS[:3], dry_run(FILES[3:]),
                                              env=self.auto_env(GITHUB_OUTPUT=value))
                self.assertEqual(code, 1)
                self.assertIn("GITHUB_OUTPUT", output)
                self.assertNotIn("pending=true", output)
                self.assertEqual(self.outputs(), "")
                self.assertFalse(Path(self.folder, "absent_output").exists())

    def test_auto_plan_rejects_malformed_existing_output_file(self):
        for existing in ("other=1", "pending=false\n", "pending_count=9\n", "pending<<EOF\ntrue\nEOF\n",
                         "x=1\npending_count<<EOF\n0\nEOF\n"):
            with self.subTest(existing=existing):
                Path(self.output).write_text(existing, encoding="utf-8")
                code, output = self.auto_plan(VERSIONS[:3], dry_run(FILES[3:]))
                self.assertEqual(code, 1)
                self.assertIn("GITHUB_OUTPUT", output)
                self.assertEqual(self.outputs(), existing)
        Path(self.output).write_bytes(b"\xff\xfe\n")
        code, output = self.auto_plan(VERSIONS[:3], dry_run(FILES[3:]))
        self.assertEqual(code, 1)
        self.assertIn("GITHUB_OUTPUT file is unreadable", output)
        self.assertEqual(Path(self.output).read_bytes(), b"\xff\xfe\n")

    def test_auto_plan_checks_output_file_before_trusting_cli_files(self):
        # Output trust is checked first, so a bad GITHUB_OUTPUT is reported even when
        # the CLI files are also broken, and no CLI file content is ever parsed.
        code, output = self.auto_plan(VERSIONS[:3], dry_run(FILES[4:]), listing="not json",
                                      env=self.auto_env(GITHUB_OUTPUT=""))
        self.assertEqual(code, 1)
        self.assertIn("GITHUB_OUTPUT", output)
        self.assertNotIn("JSON", output)

    def test_auto_plan_reports_unwritable_output_without_partial_values(self):
        # Defensive path: the file passed the trust check but the append itself fails.
        with mock.patch.object(vmp, "_write_outputs", side_effect=OSError(HOSTILE)):
            code, output = self.auto_plan(VERSIONS[:3], dry_run(FILES[3:]))
        self.assertEqual(code, 1)
        self.assertIn("GITHUB_OUTPUT could not be written", output)
        self.assertNotIn("pwned", output)
        self.assertEqual(self.outputs(), "")

    # verify-auto-applied --------------------------------------------------

    def verify(self, listing, env=None):
        list_path = self.write("after.json", listing)
        return self.run_main(["verify-auto-applied", "--migrations-dir", self.migrations,
                              "--list-json", list_path], self.auto_env() if env is None else env)

    def test_verify_auto_applied_accepts_complete_history_without_writing(self):
        code, output = self.verify(rows(VERSIONS, VERSIONS))
        self.assertEqual(code, 0, output)
        self.assertIn("complete local manifest", output)
        self.assertEqual(self.outputs(), "")

    def test_verify_auto_applied_rejects_partial_or_untrusted_history(self):
        cases = (
            (rows(VERSIONS, VERSIONS[:4]), "partial"),
            (rows(VERSIONS, VERSIONS[:3]), "partial"),
            (rows(VERSIONS, VERSIONS + ["20990101000000"]), "remote-only"),
            (rows(VERSIONS, []), "empty"),
            ("not json " + HOSTILE, "not valid JSON"),
        )
        for listing, message in cases:
            with self.subTest(message=message):
                code, output = self.verify(listing)
                self.assertEqual(code, 1)
                self.assertIn(message, output)
                self.assertNotIn("pwned", output)
                self.assertEqual(self.outputs(), "")
        code, output = self.verify(rows(VERSIONS, VERSIONS),
                                   env=self.auto_env(SUPABASE_APPROVED_BASELINE=None, GITHUB_OUTPUT=None))
        self.assertEqual(code, 1)
        self.assertIn("BASELINE", output)

    def test_verify_auto_applied_needs_no_operator_list_or_output_file(self):
        code, output = self.verify(rows(VERSIONS, VERSIONS), env={"SUPABASE_APPROVED_BASELINE": BASELINE})
        self.assertEqual(code, 0, output)


def strip_comments(text):
    """Drops full-line YAML comments so prose cannot satisfy or trip a check."""
    return "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("#"))


def step_block(text, name):
    """Returns the text of the workflow step with the given name."""
    match = re.search(rf"^      - name: {re.escape(name)}\n(.*?)(?=^      - |\Z)", text, re.M | re.S)
    if not match:
        raise AssertionError(f"step {name!r} not found")
    return match.group(1)


class DeployWorkflowTests(unittest.TestCase):
    """Static assertions on the deploy workflow; they need no network or secrets."""

    @classmethod
    def setUpClass(cls):
        cls.text = strip_comments(DEPLOY_WORKFLOW.read_text(encoding="utf-8"))

    def test_cli_is_downloaded_from_pinned_release_and_digest_checked(self):
        self.assertNotIn("supabase/setup-cli", self.text)
        step = step_block(self.text, "Install verified Supabase CLI")
        self.assertIn("SUPABASE_CLI_VERSION: 2.109.0", step)
        self.assertIn(f"SUPABASE_CLI_SHA256: {CLI_SHA256}", step)
        self.assertIn("https://github.com/supabase/cli/releases/download/v${SUPABASE_CLI_VERSION}/"
                      "supabase_${SUPABASE_CLI_VERSION}_linux_amd64.tar.gz", step)
        self.assertIn("--proto '=https'", step)
        self.assertIn("sha256sum --check --strict", step)
        # The archive must be verified before it is extracted or put on PATH.
        self.assertLess(step.index("sha256sum"), step.index("tar "))
        self.assertLess(step.index("sha256sum"), step.index("GITHUB_PATH"))

    def test_only_first_party_actions_are_used(self):
        for action in re.findall(r"uses:\s*(\S+)", self.text):
            with self.subTest(action=action):
                self.assertTrue(action.startswith("actions/"), action)

    def test_cli_telemetry_is_disabled_for_every_job(self):
        header = self.text.split("\njobs:", 1)[0]
        self.assertRegex(header, r"(?m)^env:\n(  .+\n)*  SUPABASE_TELEMETRY_DISABLED: \"1\"$")
        self.assertRegex(header, r"(?m)^env:\n(  .+\n)*  DO_NOT_TRACK: \"1\"$")

    def test_apply_step_timeout_leaves_room_for_history_verification(self):
        job = int(re.search(r"name: Apply approved migrations\n.*?timeout-minutes: (\d+)",
                            self.text, re.S).group(1))
        apply = step_block(self.text, "Apply migrations")
        verify = step_block(self.text, "Verify remote migration history")
        apply_timeout = int(re.search(r"timeout-minutes: (\d+)", apply).group(1))
        verify_timeout = int(re.search(r"timeout-minutes: (\d+)", verify).group(1))
        self.assertLess(apply_timeout + verify_timeout, job)
        self.assertIn("!cancelled()", verify)

    def test_cli_output_is_withheld_and_never_traced(self):
        self.assertNotRegex(self.text, r"set -[a-z]*x|set -o xtrace|ACTIONS_STEP_DEBUG")
        self.assertNotIn("upload-artifact", self.text)
        calls = re.findall(r"supabase (?:db push|migration list)(?:[^\n]*\\\n)*[^\n]*", self.text)
        self.assertEqual(len(calls), 4)
        for call in calls:
            with self.subTest(call=call.split("\n")[0]):
                self.assertIn('--db-url "$SUPABASE_DB_URL"', call)
                self.assertIn('> "$RUNNER_TEMP/', call)
                self.assertIn("2>", call)

    def test_no_history_rewrite_or_linked_project_commands(self):
        for forbidden in ("--include-all", "migration repair", "--linked", "supabase link",
                          "SUPABASE_ACCESS_TOKEN"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.text)

    def test_no_other_workflow_can_reach_the_hosted_database(self):
        for path in sorted(WORKFLOWS.glob("*.y*ml")):
            if path == DEPLOY_WORKFLOW:
                continue
            text = strip_comments(path.read_text(encoding="utf-8"))
            for forbidden in ("supabase db push", "supabase link", "migration repair",
                              "SUPABASE_DB_URL", "SUPABASE_ACCESS_TOKEN", "supabase-production"):
                with self.subTest(workflow=path.name, forbidden=forbidden):
                    self.assertNotIn(forbidden, text)


def top_level_block(text, key):
    """Returns the lines nested under a top-level workflow key."""
    match = re.search(rf"^{re.escape(key)}:(.*?)(?=^\S|\Z)", text, re.M | re.S)
    if not match:
        raise AssertionError(f"top-level key {key!r} not found")
    return match.group(1)


def run_script(step):
    """Returns the dedented `run: |` body of a step."""
    match = re.search(r"^        run: \|\n((?:^(?: {10}.*)?\n)+)", step, re.M)
    if not match:
        raise AssertionError("step has no block run script")
    return "\n".join(line[10:] for line in match.group(1).splitlines()) + "\n"


REPO = "incoders/zonar-hub-api"
POLICY_STEP = "Enforce dev to main promotion policy"


class PrValidationWorkflowTests(unittest.TestCase):
    """Static assertions on the PR gate; they need no network or secrets."""

    @classmethod
    def setUpClass(cls):
        cls.text = strip_comments(PR_WORKFLOW.read_text(encoding="utf-8"))

    def test_runs_trusted_base_branch_definition_for_every_change_that_targets_main(self):
        # `pull_request_target` evaluates the workflow file from the base branch, so a
        # same-repository `dev` PR cannot weaken the gate by editing this file.
        trigger = top_level_block(self.text, "on")
        self.assertEqual(re.findall(r"(?m)^  (\w+):", trigger), ["pull_request_target"])
        self.assertRegex(trigger, r"(?m)^    branches: \[main\]$")
        types = re.search(r"(?m)^    types: \[(.*)\]$", trigger).group(1)
        self.assertEqual({item.strip() for item in types.split(",")},
                         {"opened", "edited", "synchronize", "reopened", "ready_for_review"})

    def test_never_fetches_or_runs_pull_request_code(self):
        # With a base-branch trigger, checking out or fetching head code would run
        # attacker-controlled files in a trusted context; only the payload is read.
        for forbidden in ("checkout", "head.sha", "merge_commit_sha", "refs/pull/",
                          "git fetch", "git clone", "ref:", "repository:", "workflow_run",
                          "pip install", "npm ", "cache", "artifact", "GITHUB_ENV",
                          "GITHUB_OUTPUT", "GITHUB_PATH"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.text)

    def test_token_has_no_permissions(self):
        self.assertRegex(self.text, r"(?m)^permissions: \{\}$")
        self.assertEqual(len(re.findall(r"permissions:", self.text)), 1)
        self.assertNotIn("write", self.text)

    def test_exposes_one_stable_status_check(self):
        jobs = top_level_block(self.text, "jobs")
        self.assertEqual(re.findall(r"(?m)^  ([\w-]+):$", jobs), ["promotion-policy"])
        self.assertRegex(jobs, r"(?m)^    name: promotion-policy$")
        self.assertRegex(jobs, r"(?m)^    timeout-minutes: \d+$")

    def test_uses_no_secrets_actions_checkout_or_network(self):
        for forbidden in ("secrets.", "GITHUB_TOKEN", "github.token", "uses:", "curl", "wget",
                          "gh ", "urllib", "http.client", "socket", "subprocess"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.text)
        self.assertNotRegex(self.text, r"\bimport\s+requests\b")

    def test_untrusted_pull_request_data_reaches_the_step_only_through_env(self):
        step = step_block(self.text, POLICY_STEP)
        self.assertRegex(step, r"(?m)^        shell: python$")
        self.assertNotIn("${{", run_script(step))
        expressions = re.findall(r"(?m)^(.*)\$\{\{\s*github\.event\.pull_request\.(\S+)", self.text)
        self.assertTrue(expressions)
        for prefix, field in expressions:
            with self.subTest(field=field):
                if prefix == "  group: pr-validation-":
                    # The numeric PR id only names the concurrency group.
                    self.assertEqual(field, "number")
                else:
                    self.assertRegex(prefix, r"^          [A-Z_]+: $")


class PromotionPolicyTests(unittest.TestCase):
    """Runs the gate's exact Python script against PR event shapes."""

    @classmethod
    def setUpClass(cls):
        text = PR_WORKFLOW.read_text(encoding="utf-8")
        cls.text = strip_comments(text)
        cls.script = run_script(step_block(text, POLICY_STEP))

    def run_policy(self, **overrides):
        values = {"BASE_REF": "main", "HEAD_REF": "dev", "HEAD_REPO": REPO, "BASE_REPO": REPO}
        values.update(overrides)
        env = {key: value for key, value in os.environ.items()
               if key not in {"BASE_REF", "HEAD_REF", "HEAD_REPO", "BASE_REPO", "PR_BODY"}}
        env.update({key: value for key, value in values.items() if value is not None})
        with tempfile.TemporaryDirectory() as workdir:
            # The script is passed as an argv item, never through a shell.
            completed = subprocess.run([sys.executable, "-c", self.script], env=env, cwd=workdir,
                                       capture_output=True, text=True, check=False)
            self.assertEqual(os.listdir(workdir), [], "policy must not write files")
        return completed

    def assert_rejected(self, reason, **overrides):
        completed = self.run_policy(**overrides)
        self.assertEqual(completed.returncode, 1, completed.stdout + completed.stderr)
        self.assertIn("::error::", completed.stdout)
        self.assertIn(reason, completed.stdout)
        return completed

    def test_accepts_same_repository_dev_to_main_without_issue_reference(self):
        completed = self.run_policy()
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        self.assertNotIn("::error::", completed.stdout)
        self.assertNotIn("issue", completed.stdout.lower())
        self.assertNotIn("approved", completed.stdout.lower())

    def test_gate_reads_no_body_issue_or_reviewer_data(self):
        # The minimal policy only checks refs and repositories; PR prose is never read.
        for forbidden in ("pull_request.body", "PR_BODY", "issue", "reviewer", "import re"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.text)
        env_names = re.findall(r"(?m)^          ([A-Z_]+): ", step_block(self.text, POLICY_STEP))
        self.assertEqual(sorted(env_names), ["BASE_REF", "BASE_REPO", "HEAD_REF", "HEAD_REPO"])

    def test_pull_request_body_has_no_effect(self):
        for body in ("", "No issue here", "Closes #42"):
            with self.subTest(body=body):
                self.assertEqual(self.run_policy(PR_BODY=body).returncode, 0)
                self.assert_rejected("source branch", PR_BODY=body, HEAD_REF="feature/x")

    def test_rejects_forked_dev_branch(self):
        for head_repo in ("attacker/zonar-hub-api", REPO.upper(), REPO + " ", "incoders/zonar-hub-api2"):
            with self.subTest(head_repo=head_repo):
                self.assert_rejected("same repository", HEAD_REPO=head_repo)

    def test_rejects_missing_head_repository(self):
        self.assert_rejected("same repository", HEAD_REPO=None)
        self.assert_rejected("same repository", HEAD_REPO="")

    def test_rejects_missing_base_repository(self):
        self.assert_rejected("same repository", BASE_REPO=None, HEAD_REPO=None)
        self.assert_rejected("same repository", BASE_REPO="", HEAD_REPO="")

    def test_rejects_source_other_than_dev(self):
        for head in ("feature/x", "Dev", "dev2", "dev ", "refs/heads/dev", "main", "", None):
            with self.subTest(head=head):
                self.assert_rejected("source branch", HEAD_REF=head)

    def test_rejects_base_other_than_main(self):
        for base in ("dev", "main2", "Main", "refs/heads/main", "", None):
            with self.subTest(base=base):
                self.assert_rejected("target branch", BASE_REF=base)

    def test_reports_every_violation_at_once(self):
        completed = self.assert_rejected("same repository", HEAD_REPO="fork/x", HEAD_REF="main",
                                         BASE_REF="dev")
        for reason in ("source branch", "target branch"):
            self.assertIn(reason, completed.stdout)

    def test_hostile_body_and_branch_are_neither_executed_nor_echoed(self):
        payload = ('"; touch pwned; echo "$(touch pwned)`touch pwned`${{ secrets.X }}\n'
                   "::set-output name=x::y\n::stop-commands::tok\n")
        accepted = self.run_policy(PR_BODY=payload)
        self.assertEqual(accepted.returncode, 0, accepted.stdout + accepted.stderr)
        rejected = self.assert_rejected("source branch", PR_BODY=payload,
                                        HEAD_REF="dev$(touch pwned)\n::stop-commands::tok",
                                        HEAD_REPO="fork/x`touch pwned`")
        for completed in (accepted, rejected):
            output = completed.stdout + completed.stderr
            for fragment in ("pwned", "set-output", "stop-commands", "secrets.X"):
                with self.subTest(fragment=fragment):
                    self.assertNotIn(fragment, output)


def job_blocks(text):
    """Returns each job's text under `jobs:`, keyed by job id, in file order."""
    jobs = top_level_block(text, "jobs")
    parts = re.split(r"(?m)^  ([\w-]+):\n", jobs)
    return dict(zip(parts[1::2], parts[2::2]))


GATE_JOB = "verify-promotion"
GATE_STEP = "Verify main is a green CI push of a merged dev promotion"
MANUAL_ONLY = "    if: github.event_name == 'workflow_dispatch'\n"


class PromotionGateWorkflowTests(unittest.TestCase):
    """Static assertions on the credential-free CI promotion gate of the deploy workflow."""

    @classmethod
    def setUpClass(cls):
        cls.text = strip_comments(DEPLOY_WORKFLOW.read_text(encoding="utf-8"))
        cls.jobs = job_blocks(cls.text)
        cls.gate = cls.jobs[GATE_JOB]

    def test_triggers_are_manual_dispatch_and_completed_main_ci_runs_only(self):
        trigger = top_level_block(self.text, "on")
        self.assertEqual(re.findall(r"(?m)^  (\w+):", trigger), ["workflow_dispatch", "workflow_run"])
        workflow_run = trigger.split("  workflow_run:\n", 1)[1]
        self.assertEqual(re.findall(r"(?m)^    (\w+): (.*)$", workflow_run),
                         [("workflows", "[CI]"), ("types", "[completed]"), ("branches", "[main]")])

    def test_manual_jobs_cannot_run_on_workflow_run_events(self):
        self.assertEqual(list(self.jobs), ["preflight", "deploy", GATE_JOB])
        for job in ("preflight", "deploy"):
            with self.subTest(job=job):
                self.assertEqual(re.findall(r"(?m)^    if: .*\n", self.jobs[job]), [MANUAL_ONLY])
        self.assertRegex(self.jobs["deploy"], r"(?m)^    needs: preflight$")

    def test_gate_runs_only_for_successful_same_repository_main_push_ci(self):
        condition = re.search(r"(?m)^    if: >-\n((?:^      .*\n)+)", self.gate).group(1)
        clauses = {clause.strip() for clause in condition.replace("\n", " ").split("&&")}
        self.assertEqual(clauses, {
            "github.event_name == 'workflow_run'",
            "github.event.workflow_run.conclusion == 'success'",
            "github.event.workflow_run.event == 'push'",
            "github.event.workflow_run.head_branch == 'main'",
            "github.event.workflow_run.head_repository.full_name == github.repository",
        })
        self.assertNotIn("||", condition)

    def test_gate_token_can_only_read_contents_and_pull_requests(self):
        permissions = re.search(r"(?m)^    permissions:\n((?:^      .*\n)+)", self.gate).group(1)
        self.assertEqual(permissions, "      contents: read\n      pull-requests: read\n")
        self.assertEqual(top_level_block(self.text, "permissions").strip(), "contents: read")
        self.assertNotIn("write", self.text)

    def test_gate_has_no_environment_secrets_database_or_deploy_coupling(self):
        for forbidden in ("environment:", "secrets.", "vars.", "SUPABASE_DB_URL", "SUPABASE_PROJECT_REF",
                          "SUPABASE_APPROVED_BASELINE", "supabase db", "supabase migration",
                          "supabase-cli", "needs:", "auto-plan", "GITHUB_OUTPUT", "GITHUB_ENV"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.gate)
        for job, body in self.jobs.items():
            with self.subTest(job=job):
                self.assertNotIn(GATE_JOB, re.findall(r"(?m)^    needs: (.*)$", body))

    def test_gate_checks_out_the_exact_ci_head_without_credentials(self):
        checkout = re.search(r"(?m)^      - uses: actions/checkout@v4\n((?:^        .*\n)+)", self.gate).group(1)
        self.assertEqual(checkout, "        with:\n"
                                   "          ref: ${{ github.event.workflow_run.head_sha }}\n"
                                   "          persist-credentials: false\n")
        self.assertEqual(len(re.findall(r"\bref:", self.text)), 1)
        for forbidden in ("pull_request", "refs/pull/", "head_repository.clone", "repository:",
                          "git fetch", "git clone", "--paginate"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.text)

    def test_gate_calls_github_api_read_only_with_one_bounded_page(self):
        script = run_script(step_block(self.gate, GATE_STEP))
        calls = re.findall(r"gh api[^\n]*", script)
        self.assertEqual(len(calls), 2)
        for call in calls:
            with self.subTest(call=call):
                self.assertIn("gh api --method GET ", call)
                self.assertNotRegex(call, r"\s(-f|-F|--field|--raw-field|--input|-X)\b")
        self.assertIn('"repos/${GITHUB_REPOSITORY}/git/ref/heads/main"', script)
        self.assertIn('"repos/${GITHUB_REPOSITORY}/commits/${head_sha}/pulls?per_page=100"', script)
        self.assertIn("check-ci-promotion", script)

    def test_event_data_reaches_the_gate_script_only_through_files(self):
        step = step_block(self.gate, GATE_STEP)
        self.assertNotIn("${{", run_script(step))
        self.assertEqual(re.findall(r"\$\{\{[^}]*\}\}", step), ["${{ github.token }}"])

    def test_gate_never_displaces_a_queued_manual_deployment(self):
        group = re.search(r"(?m)^  group: >-\n((?:^    .*\n)+)", top_level_block(self.text, "concurrency") + "\n")
        self.assertIsNotNone(group)
        expression = " ".join(group.group(1).split())
        self.assertEqual(expression,
                         "${{ github.event_name == 'workflow_dispatch' && 'supabase-production-migrations' "
                         "|| format('supabase-ci-promotion-{0}', github.event.workflow_run.head_sha) }}")
        self.assertRegex(top_level_block(self.text, "concurrency"), r"(?m)^  cancel-in-progress: false$")


def _posix(path):
    return str(path).replace("\\", "/")


@unittest.skipUnless(shutil.which("bash") and shutil.which("jq"), "bash and jq are required")
class PromotionGateScriptTests(unittest.TestCase):
    """Runs the gate's exact shell step against fake `gh`/`git` and GitHub event shapes."""

    @classmethod
    def setUpClass(cls):
        text = DEPLOY_WORKFLOW.read_text(encoding="utf-8")
        cls.script = run_script(step_block(job_blocks(text)[GATE_JOB], GATE_STEP))

    def run_gate(self, event=None, pulls=None, remote=SHA, checkout=SHA, gh_fails=False):
        event = {"workflow_run": ci_run()} if event is None else event
        pulls = [merged_pr()] if pulls is None else pulls
        with tempfile.TemporaryDirectory() as temp:
            temp = Path(temp)
            bin_dir = temp / "bin"
            bin_dir.mkdir()
            (temp / "event.json").write_text(json.dumps(event), encoding="utf-8")
            (temp / "pulls.json").write_text(pulls if isinstance(pulls, str) else json.dumps(pulls),
                                             encoding="utf-8")
            log = temp / "gh.log"
            env_log = temp / "gh-env.log"
            config_dir = temp / "gh-config"
            config_dir.mkdir()
            pulls_answer = ('echo "HTTP 401: Bad credentials" >&2; exit 1' if gh_fails
                            else f'cat "{_posix(temp / "pulls.json")}"')
            fakes = {
                # Records every call and its gh environment; answers the two
                # read-only endpoints the gate may use.
                "gh": (f'printf "%s\\n" "$*" >> "{_posix(log)}"\n'
                       f'printf "%s\\n" "$GH_CONFIG_DIR" "$GH_HOST" "$GH_TOKEN" > "{_posix(env_log)}"\n'
                       'case "$*" in\n'
                       f'  *git/ref/heads/main*) printf "%s\\n" "{remote}" ;;\n'
                       f'  */pulls?per_page=100*) {pulls_answer} ;;\n'
                       "  *) exit 9 ;;\n"
                       "esac\n"),
                "git": f'[ "$*" = "rev-parse HEAD" ] && printf "%s\\n" "{checkout}"\n',
                "python3": f'exec "{_posix(sys.executable)}" "$@"\n',
            }
            for name, body in fakes.items():
                (bin_dir / name).write_text("#!/bin/sh\n" + body, encoding="utf-8", newline="\n")
                (bin_dir / name).chmod(0o755)
            env = {key: value for key, value in os.environ.items()
                   if not key.startswith(("GITHUB_", "GH_", "SUPABASE_"))}
            # Secondary defense behind the PATH guard: a real gh would find no
            # stored credentials and could not resolve the reserved host.
            env.update({"GITHUB_REPOSITORY": CI_REPO, "GITHUB_EVENT_PATH": _posix(temp / "event.json"),
                        "RUNNER_TEMP": _posix(temp), "GH_TOKEN": "fake-token",
                        "GH_CONFIG_DIR": _posix(config_dir), "GH_HOST": "gh-host.invalid"})
            # `cd`/`pwd` yields a PATH-safe directory on Windows too (no drive colon).
            # The guard aborts before the script if the real gh or git would run.
            prelude = (f'fake="$(cd "{_posix(bin_dir)}" && pwd)"\nexport PATH="$fake:$PATH"\n'
                       '[ "$(command -v gh)" = "$fake/gh" ] && [ "$(command -v git)" = "$fake/git" ]'
                       ' || exit 97\n')
            completed = subprocess.run(
                [shutil.which("bash"), "-c", prelude + self.script],
                env=env, cwd=REPO_ROOT, capture_output=True, text=True, check=False)
            calls = log.read_text(encoding="utf-8").splitlines() if log.exists() else []
            self.gh_env = env_log.read_text(encoding="utf-8").splitlines() if env_log.exists() else []
        self.assertNotEqual(completed.returncode, 97, "fake gh/git were not first on PATH")
        return completed, calls

    def assert_rejected(self, reason, **kwargs):
        completed, calls = self.run_gate(**kwargs)
        output = completed.stdout + completed.stderr
        self.assertNotEqual(completed.returncode, 0, output)
        self.assertIn(reason, output)
        self.assertNotIn("CI promotion verified", output)
        return calls, output

    def test_accepts_green_main_push_ci_of_merged_dev_promotion(self):
        completed, calls = self.run_gate()
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        self.assertIn("CI promotion verified", completed.stdout)
        self.assertEqual(sorted(calls), sorted([
            f"api --method GET repos/{CI_REPO}/commits/{SHA}/pulls?per_page=100",
            f"api --method GET repos/{CI_REPO}/git/ref/heads/main --jq .object.sha",
        ]))

    def test_rejects_invalid_ci_head_sha_before_calling_github(self):
        missing = ci_run()
        del missing["head_sha"]
        events = ({"workflow_run": ci_run(head_sha=SHA.upper())}, {"workflow_run": ci_run(head_sha=SHA[:7])},
                  {"workflow_run": ci_run(head_sha=SHA + "\n")}, {"workflow_run": ci_run(head_sha=f"{SHA}/x")},
                  {"workflow_run": ci_run(head_sha="../" + SHA[3:])}, {"workflow_run": ci_run(head_sha=1)},
                  {"workflow_run": missing}, {"workflow_run": None}, {"workflow_run": [ci_run()]}, {})
        for event in events:
            with self.subTest(event=event):
                calls, _ = self.assert_rejected("::error::", event=event)
                self.assertEqual(calls, [])

    def test_rejects_a_full_page_of_associated_pull_requests_as_ambiguous(self):
        others = [merged_pr(number, merged_at=None, merge_commit_sha=OTHER_SHA) for number in range(8, 107)]
        calls, output = self.assert_rejected("full page", pulls=[merged_pr()] + others)
        self.assertNotIn("git/ref/heads/main", "\n".join(calls))
        completed, _ = self.run_gate(pulls=[merged_pr()] + others[:98])
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)

    def test_rejects_malformed_pull_request_responses(self):
        for pulls in ("{not json", "{}", '"pulls"', "null"):
            with self.subTest(pulls=pulls):
                self.assert_rejected("::error::", pulls=pulls)

    def test_fails_closed_for_stale_direct_fork_or_unmerged_promotions(self):
        cases = (
            ({"remote": OTHER_SHA}, "stale"),
            ({"checkout": OTHER_SHA}, "checked out"),
            ({"pulls": []}, "direct pushes"),
            ({"pulls": [merged_pr(merged_at=None)]}, "direct pushes"),
            ({"pulls": [merged_pr(head={"ref": "dev", "repo": {"full_name": FORK}})]}, "same-repository"),
            ({"pulls": [merged_pr(head={"ref": "feature/x", "repo": {"full_name": CI_REPO}})]}, "dev to main"),
            ({"event": {"workflow_run": ci_run(event="pull_request")}}, "push"),
            ({"event": {"workflow_run": ci_run(conclusion="failure")}}, "successful"),
            ({"event": {"workflow_run": ci_run(head_repository={"full_name": FORK})}}, "repository"),
        )
        for kwargs, reason in cases:
            with self.subTest(kwargs=kwargs):
                self.assert_rejected(reason, **kwargs)

    def test_gh_runs_against_an_isolated_config_and_unroutable_host(self):
        self.run_gate()
        config_dir, host, token = self.gh_env
        self.assertTrue(config_dir.endswith("/gh-config"), config_dir)
        self.assertEqual(host, "gh-host.invalid")
        self.assertEqual(token, "fake-token")

    def test_gh_api_failure_fails_closed_before_the_verifier(self):
        calls, output = self.assert_rejected("HTTP 401", gh_fails=True)
        self.assertEqual(calls, [f"api --method GET repos/{CI_REPO}/commits/{SHA}/pulls?per_page=100"])
        self.assertNotIn("SUPABASE_DB_URL", self.script)

    def test_hostile_event_values_are_neither_executed_nor_echoed(self):
        hostile = "main$(touch pwned)`touch pwned`\n::stop-commands::tok"
        _, output = self.assert_rejected("main", event={"workflow_run": ci_run(head_branch=hostile)})
        self.assertNotIn("stop-commands", output)
        self.assertNotIn("pwned", output)
        self.assertFalse((REPO_ROOT / "pwned").exists())


if __name__ == "__main__":
    unittest.main()
