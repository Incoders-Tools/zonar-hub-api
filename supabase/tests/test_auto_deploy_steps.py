"""Runs the automatic deploy job's exact step scripts against fake gh, git and Supabase CLIs.

Standard library only; no network, database or secrets. Run with:
python -m unittest discover -s supabase/tests -p "test_*.py"
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from test_verify_migration_plan import (
    APPLY_STEP, CI_REPO, CONFIG_STEP, DB_STEPS, DEPLOY_JOB, DEPLOY_WORKFLOW, DRY_RUN_STEP, GOOD_URL,
    HISTORY_STEP, OTHER_REF, OTHER_SHA, PLAN_STEP, REBIND_AFTER_GATE, REBIND_BEFORE_APPLY, REF, REPO_ROOT,
    SECRET, SHA, VERIFY_STEP, _posix, ci_run, dry_run, job_blocks, rows, run_script, step_block, up_to_date,
    vmp)


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
                   if not key.startswith(("GITHUB_", "GH_", "SUPABASE_", "RUNNER_"))}
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
        self.job_env = {"SUPABASE_PROJECT_REF": REF, "SUPABASE_APPROVED_BASELINE": self.versions[0],
                        "GITHUB_OUTPUT": _posix(self.output), "GITHUB_REPOSITORY": CI_REPO,
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
        environment = {**self.job_env, **({"SUPABASE_DB_URL": GOOD_URL} if name in DB_STEPS else {}), **env}
        completed = run_bash_step(run_script(step_block(self.deploy, name)), self.temp, fakes,
                                  {key: value for key, value in environment.items() if value is not None})
        output = completed.stdout + completed.stderr
        self.assertNotIn(SECRET, output)
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


if __name__ == "__main__":
    unittest.main()
