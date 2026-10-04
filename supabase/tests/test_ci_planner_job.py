"""Static checks that CI runs the migration planner tests that gate deployment.

Standard library only. The deploy workflow trusts a green CI run on main, so the
`migration-planner-tests` job must exist, run the whole planner suite and stay
free of credentials.
"""

from __future__ import annotations

import ast
import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CI_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "ci.yml"
DEPLOY_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "deploy-supabase-migrations.yml"
PLANNER_SCRIPT = REPO_ROOT / "supabase" / "scripts" / "verify_migration_plan.py"
PLANNER_JOB = "migration-planner-tests"
PLANNER_COMMAND = 'python3 -m unittest discover -s supabase/tests -p "test_*.py"'
MANUAL_SUBCOMMANDS = ("check-inputs", "check-context", "check-db-url", "check-plan", "verify-applied")
AUTO_SUBCOMMANDS = ("check-ci-promotion", "check-main-head", "check-auto-config", "auto-plan",
                    "verify-auto-applied")


def job_block(text: str, name: str) -> str:
    """Return the lines of one job under `jobs:`, comments removed."""
    match = re.search(rf"(?ms)^  {re.escape(name)}:\n(.*?)(?=^  \S|\Z)", text)
    if match is None:
        raise AssertionError(f"job {name!r} not found")
    return "\n".join(line for line in match.group(1).splitlines() if not line.lstrip().startswith("#"))


class CiPlannerJobTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.text = CI_WORKFLOW.read_text(encoding="utf-8")
        cls.job = job_block(cls.text, PLANNER_JOB)

    def test_workflow_permissions_are_read_only(self):
        self.assertRegex(self.text, r"(?m)^permissions:\n  contents: read\n(?!  )")

    def test_job_runs_the_full_planner_suite(self):
        self.assertEqual(re.findall(r"(?m)^        run: (.*)$", self.job), [PLANNER_COMMAND])

    def test_job_checks_out_without_persisted_credentials(self):
        self.assertRegex(self.job, r"(?m)^      - uses: actions/checkout@v4\n        with:\n          persist-credentials: false$")

    def test_job_uses_no_secrets_or_elevated_permissions(self):
        for forbidden in ("secrets.", "vars.", "environment:", "permissions:", "SUPABASE_",
                          "continue-on-error", "if:", "${{"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.job)


class PlannerDocstringTests(unittest.TestCase):
    def test_docstring_matches_the_subcommands_the_deploy_workflow_uses(self):
        deploy = DEPLOY_WORKFLOW.read_text(encoding="utf-8")
        for command in AUTO_SUBCOMMANDS:
            with self.subTest(command=command):
                self.assertIn(f"verify_migration_plan.py {command}", deploy)
        for command in MANUAL_SUBCOMMANDS:
            with self.subTest(command=command):
                self.assertNotIn(f"verify_migration_plan.py {command}", deploy)
        docstring = " ".join((ast.get_docstring(ast.parse(PLANNER_SCRIPT.read_text(encoding="utf-8"))) or "").split())
        self.assertIn("The last five back the automatic deploy workflow", docstring)
        self.assertIn("The first five are not referenced by any workflow.", docstring)
        self.assertNotIn("later change", docstring)


if __name__ == "__main__":
    unittest.main()
