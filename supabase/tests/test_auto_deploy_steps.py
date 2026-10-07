"""Runs the automatic deploy job's exact step scripts against fake gh, git and Supabase CLIs.

Standard library only; no network, database or secrets. Run with:
python -m unittest discover -s supabase/tests -p "test_*.py"
"""

import base64
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from test_verify_migration_plan import (
    APPLY_STEP, CA_FILE, CA_RELATIVE, CI_REPO, CONFIG_STEP, DB_SECRET_ENV, DB_STEPS, DEPLOY_JOB, DEPLOY_STEPS,
    DEPLOY_WORKFLOW, DRY_RUN_STEP, GOOD_URL, HISTORY_STEP, OTHER_REF, OTHER_SHA, PLAN_STEP, POOLER_HOST,
    POOLER_URL, REBIND_AFTER_GATE, REBIND_BEFORE_APPLY, REF, REPO_ROOT, SECRET, SHA, VERIFY_STEP, _posix, ci_run,
    dry_run, job_blocks, rows, run_script, step_block, strip_comments, up_to_date, vmp)

README = REPO_ROOT / "supabase" / "README-migration-delivery.md"
# Recorded independently of the planner so a changed constant cannot pass silently.
CA_DER_SHA256 = "807025ad50d4ed219d2c9c7d299c004f824eb00cf7f65afef607d07b72e6cafa"
CA_ENV = "          PGSSLROOTCERT: ${{ github.workspace }}/" + CA_RELATIVE + "\n"
# GitHub sets GITHUB_WORKSPACE to the checkout directory; the harness runs steps there.
WORKSPACE = _posix(REPO_ROOT)
CLI_CALLS = [
    'supabase migration list --db-url "$SUPABASE_DB_URL" --output-format json \\',
    'supabase db push --dry-run --db-url "$SUPABASE_DB_URL" \\',
    'supabase db push --yes --db-url "$SUPABASE_DB_URL" \\',
    'supabase migration list --db-url "$SUPABASE_DB_URL" --output-format json \\',
]


def step_env(step, url):
    """Returns a step's `env:` mapping with its expressions resolved as the runner would.

    Only the expressions the deploy job is allowed to use are known; any other one
    fails the test instead of silently reaching a script.
    """
    match = re.search(r"(?m)^        env:\n((?:^          .*\n)+)", step)
    expressions = {"${{ secrets.SUPABASE_DB_URL }}": url, "${{ github.workspace }}": WORKSPACE,
                   "${{ github.token }}": "fake-token"}
    env = {}
    for line in match.group(1).splitlines() if match else ():
        key, _, value = line.strip().partition(": ")
        for expression, resolved in expressions.items():
            value = value.replace(expression, resolved)
        if "${{" in value:
            raise AssertionError(f"unexpected expression in {key}")
        env[key] = value
    return env


def run_bash_step(script, temp, fakes, env):
    """Runs a workflow step script in bash from the repository root with fake commands.

    `fakes` maps command names to POSIX sh bodies written to `temp/bin`; `python3`
    always runs this interpreter. The prelude exits 97 before the script when bash
    would resolve any of them to a real binary, so a real gh, git or supabase never
    runs. As a secondary defense a real gh would find no stored credentials and
    could not resolve the reserved host.
    """
    bin_dir = temp / "bin"
    bin_dir.mkdir(exist_ok=True)
    config_dir = temp / "gh-config"
    config_dir.mkdir(exist_ok=True)
    fakes = {**fakes, "python3": f'exec "{_posix(sys.executable)}" "$@"\n'}
    for name, body in fakes.items():
        (bin_dir / name).write_text("#!/bin/sh\n" + body, encoding="utf-8", newline="\n")
        (bin_dir / name).chmod(0o755)
    environment = {key: value for key, value in os.environ.items()
                   if not key.startswith(("GITHUB_", "GH_", "SUPABASE_", "RUNNER_", "PG"))}
    environment.update({"RUNNER_TEMP": _posix(temp), "GH_TOKEN": "fake-token",
                        "GH_CONFIG_DIR": _posix(config_dir), "GH_HOST": "gh-host.invalid"})
    environment.update(env)
    # `cd`/`pwd` yields a PATH-safe directory on Windows too (no drive colon).
    guard = " && ".join(f'[ "$(command -v {name})" = "$fake/{name}" ]' for name in sorted(fakes))
    prelude = f'fake="$(cd "{_posix(bin_dir)}" && pwd)"\nexport PATH="$fake:$PATH"\n{guard} || exit 97\n'
    completed = subprocess.run([shutil.which("bash"), "-c", prelude + script], env=environment,
                               cwd=REPO_ROOT, capture_output=True, text=True, check=False)
    if completed.returncode == 97:
        raise AssertionError("fake commands were not first on PATH")
    return completed


def fake_supabase(state):
    """A fake Supabase CLI that serves history and dry-run files from `state`.

    Every call is logged; failures print the database URL so tests can prove the
    workflow withholds CLI output. A push copies `after.json` over the history first,
    so a failing push can still leave some migrations applied.
    """
    s = _posix(state)
    return (f'printf "%s\\n" "$*" >> "{s}/supabase.log"\n'
            f'printf "%s\\n" "${{PGSSLROOTCERT-unset}}" >> "{s}/ca.log"\n'
            'case "$*" in\n'
            f'  "migration list "*) [ -f "{s}/list-fails" ] && {{ echo "dial $SUPABASE_DB_URL" >&2; exit 1; }}\n'
            f'    cat "{s}/list.json" ;;\n'
            f'  "db push --dry-run "*) [ -f "{s}/dry-fails" ] && {{ echo "failed $SUPABASE_DB_URL" >&2; exit 1; }}\n'
            f'    cat "{s}/dry.log" ;;\n'
            f'  "db push --yes "*) cp "{s}/after.json" "{s}/list.json"\n'
            f'    [ -f "{s}/push-fails" ] && {{ echo "ERROR: $SUPABASE_DB_URL" >&2; exit 1; }}\n'
            '    echo "Applying migration $SUPABASE_DB_URL" ;;\n'
            "  *) exit 9 ;;\n"
            "esac\n")


@unittest.skipUnless(shutil.which("bash"), "bash is required")
class AutoDeployScriptTests(unittest.TestCase):
    """Runs the deploy job's exact step scripts against fake gh, git and Supabase CLIs.

    The planner reads the real `supabase/migrations` manifest, as it does on the runner.
    """

    @classmethod
    def setUpClass(cls):
        cls.deploy = job_blocks(DEPLOY_WORKFLOW.read_text(encoding="utf-8"))[DEPLOY_JOB]
        manifest = vmp.read_manifest(str(REPO_ROOT / "supabase" / "migrations"))
        cls.versions = [entry.version for entry in manifest]
        cls.files = [entry.filename for entry in manifest]

    def setUp(self):
        workdir = tempfile.TemporaryDirectory()
        self.addCleanup(workdir.cleanup)
        self.temp = Path(workdir.name)
        self.state = self.temp / "state"
        self.state.mkdir()
        self.output = self.temp / "github_output"
        self.output.write_text("", encoding="utf-8")
        (self.temp / "event.json").write_text(json.dumps({"workflow_run": ci_run()}), encoding="utf-8")
        self.remote_main = SHA
        self.checkout = SHA
        self.url = GOOD_URL
        self.job_env = {"SUPABASE_PROJECT_REF": REF, "SUPABASE_APPROVED_BASELINE": self.versions[0],
                        "GITHUB_OUTPUT": _posix(self.output), "GITHUB_REPOSITORY": CI_REPO,
                        "GITHUB_WORKSPACE": WORKSPACE,
                        "GITHUB_EVENT_PATH": _posix(self.temp / "event.json")}

    def prepare(self, remote, dry_log, after=None, fail=()):
        self.write("list.json", rows(self.versions, remote))
        self.write("dry.log", dry_log)
        self.write("after.json", rows(self.versions, self.versions if after is None else after))
        for name in fail:
            self.write(f"{name}-fails", "")

    def write(self, name, text):
        (self.state / name).write_text(text, encoding="utf-8")

    def log(self, name):
        path = self.state / name
        return path.read_text(encoding="utf-8").splitlines() if path.exists() else []

    def step(self, name, **env):
        fakes = {
            "gh": (f'printf "%s\\n" "$*" >> "{_posix(self.state)}/gh.log"\n'
                   f'[ -f "{_posix(self.state)}/gh-fails" ] && {{ echo "HTTP 401: Bad credentials" >&2; exit 1; }}\n'
                   'case "$*" in\n'
                   f'  "api --method GET repos/{CI_REPO}/git/ref/heads/main --jq .object.sha")'
                   f' printf "%s\\n" "{self.remote_main}" ;;\n'
                   "  *) exit 9 ;;\n"
                   "esac\n"),
            "git": f'[ "$*" = "rev-parse HEAD" ] && printf "%s\\n" "{self.checkout}"\n',
            "supabase": fake_supabase(self.state),
        }
        block = step_block(self.deploy, name)
        environment = {**self.job_env, **step_env(block, self.url), **env}
        completed = run_bash_step(run_script(block), self.temp, fakes,
                                  {key: value for key, value in environment.items() if value is not None})
        output = completed.stdout + completed.stderr
        self.assertNotIn(SECRET, output)
        self.assertNotIn(POOLER_HOST, output)
        self.assertNotIn("Applying migration", output)
        return completed

    def plan_outputs(self):
        lines = self.output.read_text(encoding="utf-8").splitlines()
        return dict(line.split("=", 1) for line in lines)

    def run_flow(self):
        """Runs the database steps in workflow order, mirroring their asserted `if:` conditions."""
        ran = {}
        for name in (HISTORY_STEP, DRY_RUN_STEP, PLAN_STEP):
            ran[name] = self.step(name)
            if ran[name].returncode:
                return ran
        if self.plan_outputs().get("pending") == "true":
            for name in (REBIND_BEFORE_APPLY, APPLY_STEP):
                ran[name] = self.step(name)
                if ran[name].returncode:
                    break
            if APPLY_STEP in ran:
                # `!cancelled() && steps.push.outcome != 'skipped'`: runs even after a failed push.
                ran[VERIFY_STEP] = self.step(VERIFY_STEP)
        return ran

    def pushes(self):
        return [call for call in self.log("supabase.log") if call.startswith("db push --yes")]

    def test_pending_migrations_are_pushed_once_and_verified_against_the_full_manifest(self):
        self.prepare(self.versions[:-2], dry_run(self.files[-2:]))
        ran = self.run_flow()
        self.assertEqual(list(ran), [HISTORY_STEP, DRY_RUN_STEP, PLAN_STEP, REBIND_BEFORE_APPLY, APPLY_STEP,
                                     VERIFY_STEP])
        for name, completed in ran.items():
            with self.subTest(step=name):
                self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        self.assertEqual(self.plan_outputs(), {"pending": "true", "pending_count": "2"})
        self.assertIn(self.files[-1], ran[PLAN_STEP].stdout)
        self.assertEqual(self.pushes(), [f"db push --yes --db-url {GOOD_URL}"])
        self.assertIn("complete local manifest", ran[VERIFY_STEP].stdout)
        for call in self.log("supabase.log"):
            with self.subTest(call=call.split(" --db-url")[0]):
                self.assertNotRegex(call, r"--include-all|repair|link|seed")

    def test_no_op_plan_skips_rebind_push_and_postcheck(self):
        self.prepare(self.versions, up_to_date())
        ran = self.run_flow()
        self.assertEqual(list(ran), [HISTORY_STEP, DRY_RUN_STEP, PLAN_STEP])
        self.assertEqual(ran[PLAN_STEP].returncode, 0, ran[PLAN_STEP].stdout + ran[PLAN_STEP].stderr)
        self.assertIn("no migrations are pending", ran[PLAN_STEP].stdout)
        self.assertEqual(self.plan_outputs(), {"pending": "false", "pending_count": "0"})
        self.assertEqual([call.split(" --db-url")[0] for call in self.log("supabase.log")],
                         ["migration list", "db push --dry-run"])
        self.assertEqual(self.log("gh.log"), [])

    def test_failed_push_still_rechecks_history_and_withholds_cli_output(self):
        for applied, verified in ((self.versions[:-1], 1), (self.versions, 0)):
            with self.subTest(applied=len(applied)):
                (self.state / "supabase.log").unlink(missing_ok=True)
                self.output.write_text("", encoding="utf-8")
                self.prepare(self.versions[:-2], dry_run(self.files[-2:]), after=applied, fail=("push",))
                ran = self.run_flow()
                self.assertEqual(ran[APPLY_STEP].returncode, 1)
                self.assertIn("push failed; output withheld", ran[APPLY_STEP].stdout)
                self.assertNotIn("ERROR:", ran[APPLY_STEP].stdout + ran[APPLY_STEP].stderr)
                self.assertEqual(len(self.pushes()), 1)
                self.assertEqual(ran[VERIFY_STEP].returncode, verified,
                                 ran[VERIFY_STEP].stdout + ran[VERIFY_STEP].stderr)
                if verified:
                    self.assertIn("partial", ran[VERIFY_STEP].stderr)

    def test_cli_read_failures_stop_before_planning_and_withhold_output(self):
        for failing, step in (("list", HISTORY_STEP), ("dry", DRY_RUN_STEP)):
            with self.subTest(failing=failing):
                for leftover in ("list-fails", "dry-fails"):
                    (self.state / leftover).unlink(missing_ok=True)
                self.prepare(self.versions[:-2], dry_run(self.files[-2:]), fail=(failing,))
                ran = self.run_flow()
                self.assertEqual(list(ran)[-1], step)
                self.assertEqual(ran[step].returncode, 1)
                self.assertIn("output withheld", ran[step].stdout)
                self.assertNotIn("dial", ran[step].stdout + ran[step].stderr)
                self.assertEqual(self.pushes(), [])
                self.assertEqual(self.plan_outputs(), {})

    def test_untrustworthy_plan_writes_no_outputs_and_never_pushes(self):
        cases = (
            (self.versions[:-2], dry_run(self.files[-1:]), "does not match"),
            (self.versions[:-2], up_to_date(), "up to date"),
            (self.versions + ["20990101000000"], up_to_date(), "remote-only"),
        )
        for remote, log, message in cases:
            with self.subTest(message=message):
                self.prepare(remote, log)
                ran = self.run_flow()
                self.assertEqual(list(ran)[-1], PLAN_STEP)
                self.assertEqual(ran[PLAN_STEP].returncode, 1)
                self.assertIn(message, ran[PLAN_STEP].stderr)
                self.assertEqual(self.plan_outputs(), {})
                self.assertEqual(self.pushes(), [])

    def test_main_moving_before_apply_prevents_the_push(self):
        self.prepare(self.versions[:-2], dry_run(self.files[-2:]))
        self.remote_main = OTHER_SHA
        ran = self.run_flow()
        self.assertEqual(list(ran)[-1], REBIND_BEFORE_APPLY)
        self.assertEqual(ran[REBIND_BEFORE_APPLY].returncode, 1)
        self.assertIn("stale", ran[REBIND_BEFORE_APPLY].stderr)
        self.assertEqual(self.pushes(), [])

    def test_rebind_steps_bind_the_ci_head_to_the_checkout_and_current_main(self):
        for name in (REBIND_AFTER_GATE, REBIND_BEFORE_APPLY):
            with self.subTest(step=name):
                (self.state / "gh.log").unlink(missing_ok=True)
                completed = self.step(name)
                self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
                self.assertEqual(self.log("gh.log"),
                                 [f"api --method GET repos/{CI_REPO}/git/ref/heads/main --jq .object.sha"])
        cases = (
            ({"remote_main": OTHER_SHA}, None, "stale"),
            ({"remote_main": ""}, None, "stale"),
            ({"checkout": OTHER_SHA}, None, "checked out"),
            ({}, {"workflow_run": ci_run(head_sha="main$(touch pwned)`touch pwned`")}, "::error::"),
            ({}, {}, "::error::"),
        )
        for attributes, event, message in cases:
            with self.subTest(attributes=attributes, event=event):
                self.remote_main, self.checkout = SHA, SHA
                for key, value in attributes.items():
                    setattr(self, key, value)
                if event is not None:
                    (self.temp / "event.json").write_text(json.dumps(event), encoding="utf-8")
                completed = self.step(REBIND_AFTER_GATE)
                self.assertNotEqual(completed.returncode, 0)
                self.assertIn(message, completed.stderr)
                self.assertNotIn("pwned", completed.stdout + completed.stderr)
                self.assertFalse((REPO_ROOT / "pwned").exists())

    def test_rebind_fails_closed_when_github_cannot_be_read(self):
        self.write("gh-fails", "")
        completed = self.step(REBIND_AFTER_GATE)
        self.assertNotEqual(completed.returncode, 0)
        self.assertIn("HTTP 401", completed.stderr)
        self.assertNotIn("current head of refs/heads/main", completed.stdout)

    def test_invalid_environment_configuration_fails_closed_before_the_cli(self):
        completed = self.step(CONFIG_STEP)
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        cases = (
            ({"SUPABASE_PROJECT_REF": None}, "project ref"),
            ({"SUPABASE_PROJECT_REF": OTHER_REF}, "host"),
            ({"SUPABASE_APPROVED_BASELINE": None}, "BASELINE"),
            ({"SUPABASE_DB_URL": None}, "missing"),
            ({"SUPABASE_DB_URL": GOOD_URL.replace("sslmode=require", "sslmode=prefer")}, "sslmode"),
        )
        for env, message in cases:
            with self.subTest(env=env):
                completed = self.step(CONFIG_STEP, **env)
                self.assertEqual(completed.returncode, 1)
                self.assertIn(message, completed.stderr)
        self.assertEqual(self.log("supabase.log"), [])

    def test_config_accepts_the_session_pooler_with_the_workflow_pinned_ca(self):
        self.url = POOLER_URL
        completed = self.step(CONFIG_STEP)
        self.assertEqual(completed.returncode, 0, completed.stdout + completed.stderr)
        self.assertIn("session pooler with verify-full TLS and the pinned CA", completed.stdout)
        cases = (
            ({"PGSSLROOTCERT": None}, "PGSSLROOTCERT"),
            ({"PGSSLROOTCERT": f"{WORKSPACE}/supabase/certs/other.crt"}, "PGSSLROOTCERT"),
            ({"GITHUB_WORKSPACE": None}, "GITHUB_WORKSPACE"),
            ({"GITHUB_WORKSPACE": _posix(self.temp)}, "PGSSLROOTCERT"),
        )
        for env, message in cases:
            with self.subTest(env=env):
                completed = self.step(CONFIG_STEP, **env)
                self.assertEqual(completed.returncode, 1)
                self.assertIn(message, completed.stderr)
        self.assertEqual(self.log("supabase.log"), [])

    def test_config_rejects_connection_services_on_both_routes_before_the_cli(self):
        for url in (GOOD_URL, POOLER_URL):
            for name, value in (("PGSERVICE", "evil"), ("PGSERVICEFILE", "/tmp/evil.conf"), ("PGSERVICE", "")):
                with self.subTest(pooler=url == POOLER_URL, name=name, value=value):
                    self.url = url
                    completed = self.step(CONFIG_STEP, **{name: value})
                    self.assertEqual(completed.returncode, 1)
                    self.assertIn(name, completed.stderr)
        self.assertEqual(self.log("supabase.log"), [])

    def test_every_cli_call_receives_the_pinned_ca_from_the_workspace(self):
        for url in (POOLER_URL, GOOD_URL):
            with self.subTest(pooler=url == POOLER_URL):
                for leftover in ("supabase.log", "ca.log"):
                    (self.state / leftover).unlink(missing_ok=True)
                self.output.write_text("", encoding="utf-8")
                self.url = url
                self.prepare(self.versions[:-1], dry_run(self.files[-1:]))
                ran = self.run_flow()
                self.assertEqual(list(ran)[-1], VERIFY_STEP)
                self.assertEqual(ran[VERIFY_STEP].returncode, 0, ran[VERIFY_STEP].stderr)
                self.assertEqual(len(self.log("supabase.log")), 4)
                self.assertEqual(self.log("ca.log"), [f"{WORKSPACE}/{CA_RELATIVE}"] * 4)


class PinnedCaWorkflowTests(unittest.TestCase):
    """Static checks that the deploy job hands the committed CA to exactly the database steps."""

    @classmethod
    def setUpClass(cls):
        cls.text = strip_comments(DEPLOY_WORKFLOW.read_text(encoding="utf-8"))
        cls.deploy = job_blocks(cls.text)[DEPLOY_JOB]

    def test_committed_ca_is_a_single_certificate_with_the_pinned_der_digest(self):
        self.assertFalse(CA_FILE.is_symlink())
        self.assertTrue(CA_FILE.is_file())
        text = CA_FILE.read_text(encoding="ascii")
        self.assertEqual(text.count("-----BEGIN CERTIFICATE-----"), 1)
        self.assertNotIn("PRIVATE KEY", text)
        body = text.split("-----BEGIN CERTIFICATE-----")[1].split("-----END CERTIFICATE-----")[0]
        der = base64.b64decode("".join(body.split()), validate=True)
        self.assertEqual(hashlib.sha256(der).hexdigest(), CA_DER_SHA256)
        self.assertEqual(vmp.POOLER_CA_DER_SHA256, CA_DER_SHA256)
        self.assertEqual(vmp.POOLER_CA_RELATIVE_PATH, CA_RELATIVE)

    def test_exactly_the_database_steps_receive_the_secret_and_the_pinned_ca(self):
        for name in DEPLOY_STEPS:
            step = step_block(self.deploy, name)
            with self.subTest(step=name):
                if name in DB_STEPS:
                    env = re.search(r"(?m)^        env:\n((?:^          .*\n)+)", step).group(1)
                    self.assertEqual(env, DB_SECRET_ENV + CA_ENV)
                else:
                    for forbidden in ("SUPABASE_DB_URL", "PGSSLROOTCERT", "github.workspace", "secrets."):
                        self.assertNotIn(forbidden, step)
        self.assertEqual(len(re.findall(r"PGSSLROOTCERT", self.text)), len(DB_STEPS))
        self.assertEqual(len(re.findall(r"github\.workspace", self.text)), len(DB_STEPS))

    def test_no_other_libpq_variable_or_runtime_environment_file_is_set(self):
        # A service, host or TLS override could replace what check-auto-config proved,
        # and GITHUB_ENV would let one step change the environment of later steps.
        self.assertEqual(re.findall(r"\bPG[A-Z]+", self.text), ["PGSSLROOTCERT"] * len(DB_STEPS))
        self.assertNotIn("GITHUB_ENV", self.text)

    def test_cli_invocations_are_unchanged(self):
        self.assertEqual(re.findall(r"(?m)^          if ! (supabase .*)$", self.deploy), CLI_CALLS)


class ReadmeConnectionTests(unittest.TestCase):
    """The runbook's connection examples must pass the real validator with placeholder values."""

    @classmethod
    def setUpClass(cls):
        cls.text = README.read_text(encoding="utf-8")

    def test_connection_examples_validate_on_both_routes_without_real_credentials(self):
        examples = re.findall(r"`(postgresql://[^`]+)`", self.text)
        self.assertEqual(len(examples), 2)
        routes = []
        for example in examples:
            with self.subTest(example=example):
                self.assertIn(":<percent-encoded-password>@", example)
                url = example.replace("<ref>", REF).replace("<percent-encoded-password>", "placeholder-only")
                routes.append(vmp.validate_db_url(url, REF))
        self.assertEqual(sorted(routes), sorted([vmp.DIRECT_ROUTE, vmp.POOLER_ROUTE]))

    def test_runbook_records_the_pooler_and_ca_facts_the_validator_enforces(self):
        for fragment in (vmp.POOLER_HOST, CA_RELATIVE, CA_DER_SHA256, "postgres.<ref>", "6543", "IPv6",
                         "PGSSLROOTCERT", "verify-full", "PGSERVICE"):
            with self.subTest(fragment=fragment):
                self.assertIn(fragment, self.text)


if __name__ == "__main__":
    unittest.main()
