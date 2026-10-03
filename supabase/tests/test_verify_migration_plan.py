"""Behavior tests for the production migration preflight (stdlib unittest only).

Run with: python -m unittest discover -s supabase/tests -p "test_*.py"
"""

import contextlib
import io
import json
import os
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import verify_migration_plan as vmp  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = REPO_ROOT / ".github" / "workflows"
DEPLOY_WORKFLOW = WORKFLOWS / "deploy-supabase-migrations.yml"
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

    def test_script_entry_point_exit_code_and_output_hide_the_secret(self):
        script = Path(vmp.__file__)
        env = {**os.environ, "SUPABASE_DB_URL": GOOD_URL.replace("sslmode=require", "sslmode=allow"),
               "SUPABASE_PROJECT_REF": REF, "INPUT_PROJECT_REF": REF}
        completed = subprocess.run([sys.executable, str(script), "check-db-url"], env=env,
                                   capture_output=True, text=True, check=False)
        self.assertEqual(completed.returncode, 1)
        self.assertIn("sslmode", completed.stderr)
        self.assertNotIn(SECRET, completed.stdout + completed.stderr)


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


if __name__ == "__main__":
    unittest.main()
