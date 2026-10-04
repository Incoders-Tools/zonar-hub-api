#!/usr/bin/env python3
"""Fail-closed preflight for the production Supabase migration workflow.

Every subcommand reads its inputs from environment variables or files written by
the Supabase CLI, validates them, and exits non-zero on any doubt. Error
messages never include the database URL or any part of its credentials.

Subcommands:
  check-inputs    validate operator dispatch inputs (no secrets involved)
  check-context   bind the run to refs/heads/main and the dispatched main SHA
  check-db-url    validate SUPABASE_DB_URL against the approved project ref
  check-plan      compare manifest, remote history, dry run and approved list
  verify-applied  confirm the remote history after `supabase db push`
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import Mapping, NamedTuple, Sequence
from urllib.parse import parse_qsl, urlsplit

MAIN_REF = "refs/heads/main"
SHA_PATTERN = re.compile(r"^[0-9a-f]{40}$")
PROJECT_REF_PATTERN = re.compile(r"^[a-z0-9]{20}$")
VERSION_PATTERN = re.compile(r"^[0-9]+$")
MIGRATION_FILE_PATTERN = re.compile(r"^([0-9]+)_[A-Za-z0-9_.-]+\.sql$")
DRY_RUN_HEADER = "Would push these migrations:"
DRY_RUN_ITEM = re.compile(r"^\s*•\s+(\S+\.sql)\s*$")
# Exact line Supabase CLI 2.109.0 prints when `db push --dry-run` has nothing to apply.
DRY_RUN_UP_TO_DATE = "Remote database is up to date."
# Diagnostic words that void a no-op signal. Benign CLI 2.109.0 output (WARN config
# notices, DRY RUN banner, Connecting, update notice) contains none of them.
DRY_RUN_DIAGNOSTIC = re.compile(r"\b(?:error|fatal|panic|failed)\b", re.IGNORECASE)
TLS_MODES = {"require", "verify-ca", "verify-full"}
# Only these libpq query parameters are allowed; host/hostaddr/port/service
# overrides could silently redirect the connection away from the checked host.
ALLOWED_QUERY_KEYS = {"sslmode", "connect_timeout"}
DIRECT_PORT = 5432
# The direct connection authenticates as `postgres` against the `postgres`
# database; pooler-style users such as `postgres.<ref>` are rejected.
DIRECT_USER = "postgres"
DIRECT_DATABASE_PATH = "/postgres"


class PlanError(Exception):
    """A validation failure whose message is safe to print in CI logs."""


class ManifestEntry(NamedTuple):
    version: str
    filename: str


class HistoryRow(NamedTuple):
    local: str
    remote: str


def parse_expected_versions(text: str) -> list[str]:
    """Parses the operator-approved pending versions; an empty list stops the run."""
    versions = [item for item in re.split(r"[\s,]+", text or "") if item]
    if not versions:
        raise PlanError("Expected pending version list is empty; nothing is approved to apply.")
    for version in versions:
        if not VERSION_PATTERN.match(version):
            raise PlanError("Expected pending versions must contain digits only.")
    if len(set(versions)) != len(versions):
        raise PlanError("Expected pending version list contains a duplicate version.")
    return versions


def read_manifest(folder: str) -> list[ManifestEntry]:
    """Reads local migrations in the same filename order the Supabase CLI uses."""
    path = Path(folder)
    if not path.is_dir():
        raise PlanError("Migrations directory does not exist.")
    entries = []
    for name in sorted(item.name for item in path.iterdir() if item.suffix == ".sql"):
        match = MIGRATION_FILE_PATTERN.match(name)
        if not match:
            raise PlanError(f"{name} is not a valid migration filename.")
        entries.append(ManifestEntry(match.group(1), name))
    if not entries:
        raise PlanError("Local manifest contains no migrations.")
    versions = [entry.version for entry in entries]
    if len(set(versions)) != len(versions):
        raise PlanError("Local manifest contains a duplicate migration version.")
    return entries


def parse_migration_list(text: str) -> list[HistoryRow]:
    """Parses `supabase migration list --output-format json` output."""
    try:
        document = json.loads(text)
    except ValueError as error:
        raise PlanError("Migration list output is not valid JSON.") from error
    items = document.get("migrations") if isinstance(document, dict) else None
    if not isinstance(items, list):
        raise PlanError("Migration list output has no migrations array.")
    result = []
    for item in items:
        if not isinstance(item, dict):
            raise PlanError("Migration list entry has an unexpected shape.")
        local, remote = item.get("local", ""), item.get("remote", "")
        if not isinstance(local, str) or not isinstance(remote, str):
            raise PlanError("Migration list entry has an unexpected shape.")
        result.append(HistoryRow(local, remote))
    return result


def parse_dry_run(text: str) -> list[str]:
    """Extracts migration filenames listed by `supabase db push --dry-run`."""
    lines = (text or "").splitlines()
    if DRY_RUN_HEADER not in (line.strip() for line in lines):
        raise PlanError("Dry run did not report any pending migrations.")
    files = [match.group(1) for match in map(DRY_RUN_ITEM.match, lines) if match]
    if not files:
        raise PlanError("Dry run reported a header but no migration files.")
    return files


def _remote_prefix(manifest: Sequence[ManifestEntry], history: Sequence[HistoryRow], baseline: str) -> list[str]:
    """Returns the remote history after proving it is a contiguous manifest prefix."""
    versions = [entry.version for entry in manifest]
    if [row.local for row in history if row.local] != versions:
        raise PlanError("CLI local migration list does not match the checked-out manifest.")
    for row in history:
        if row.remote and not row.local:
            raise PlanError("Remote history contains a remote-only migration.")
        if row.remote and row.remote != row.local:
            raise PlanError("Remote history and local manifest diverge.")
    remote = [row.remote for row in history if row.remote]
    if not remote:
        raise PlanError("Remote migration history is empty; bootstrap the approved baseline first.")
    if remote != versions[: len(remote)]:
        raise PlanError("Remote history is not a contiguous prefix of the local manifest.")
    if baseline not in versions:
        raise PlanError("Approved baseline is not part of the local manifest.")
    if baseline not in remote:
        raise PlanError("Remote history does not include the approved baseline.")
    return remote


def check_plan(
    manifest: Sequence[ManifestEntry],
    history: Sequence[HistoryRow],
    baseline: str,
    expected: Sequence[str],
    dry_run_files: Sequence[str],
) -> list[str]:
    """Returns the filenames to apply when every source agrees exactly."""
    remote = _remote_prefix(manifest, history, baseline)
    pending = list(manifest[len(remote):])
    if [entry.version for entry in pending] != list(expected):
        raise PlanError("Remote pending migrations do not match the operator-approved version list.")
    files = [entry.filename for entry in pending]
    if list(dry_run_files) != files:
        raise PlanError("Supabase dry run does not match the approved pending migrations.")
    return files


def verify_applied(
    manifest: Sequence[ManifestEntry],
    history: Sequence[HistoryRow],
    baseline: str,
    expected: Sequence[str],
) -> None:
    """Confirms the remote history now ends exactly at the last approved version."""
    try:
        remote = _remote_prefix(manifest, history, baseline)
    except PlanError as error:
        raise PlanError(f"Approved migrations are not applied as expected: {error}") from error
    if remote[-len(expected):] != list(expected):
        raise PlanError("Approved migrations are not applied as expected.")


def derive_pending(
    manifest: Sequence[ManifestEntry],
    history: Sequence[HistoryRow],
    baseline: str,
) -> list[ManifestEntry]:
    """Returns the exact manifest suffix missing remotely, or [] when the remote is complete."""
    if any(not row.local and not row.remote for row in history):
        raise PlanError("Migration list contains a malformed row with no version.")
    remote = _remote_prefix(manifest, history, baseline)
    return list(manifest[len(remote):])


def _is_up_to_date(dry_run_text: str) -> bool:
    """True only for the CLI no-op signal without any header, listed migration or error diagnostic."""
    lines = [line.strip() for line in dry_run_text.splitlines()]
    return (DRY_RUN_UP_TO_DATE in lines
            and DRY_RUN_HEADER not in lines
            and not any(DRY_RUN_ITEM.match(line) for line in dry_run_text.splitlines())
            and not any(DRY_RUN_DIAGNOSTIC.search(line) for line in lines))


def check_auto_plan(
    manifest: Sequence[ManifestEntry],
    history: Sequence[HistoryRow],
    baseline: str,
    dry_run_text: str | None,
) -> list[str]:
    """Returns the filenames to apply when the CLI dry run agrees exactly with derived pending.

    Pending migrations always require dry-run output listing exactly those files in
    order. With nothing pending the dry run may be skipped (None); when it is
    consulted it must carry the exact up-to-date signal and nothing else to apply.
    """
    pending = derive_pending(manifest, history, baseline)
    if not pending:
        if dry_run_text is not None and not _is_up_to_date(dry_run_text):
            raise PlanError("Dry run is not the exact up to date signal although nothing is pending.")
        return []
    if dry_run_text is None:
        raise PlanError("Supabase dry run output is required when migrations are pending.")
    if DRY_RUN_UP_TO_DATE in (line.strip() for line in dry_run_text.splitlines()):
        raise PlanError("Supabase dry run reports up to date although migrations are pending.")
    files = [entry.filename for entry in pending]
    if parse_dry_run(dry_run_text) != files:
        raise PlanError("Supabase dry run does not match the derived pending migrations.")
    return files


def verify_auto_applied(
    manifest: Sequence[ManifestEntry],
    history: Sequence[HistoryRow],
    baseline: str,
) -> None:
    """Confirms the remote history now equals the complete local manifest."""
    try:
        pending = derive_pending(manifest, history, baseline)
    except PlanError as error:
        raise PlanError(f"Migrations are not applied as expected: {error}") from error
    if pending:
        raise PlanError("Migrations are not applied as expected: remote history is partial.")


def validate_project_ref(project_ref: str) -> None:
    if not PROJECT_REF_PATTERN.match(project_ref or ""):
        raise PlanError("Supabase project ref must be 20 lowercase alphanumeric characters.")


def validate_db_url(url: str, project_ref: str) -> None:
    """Requires the direct postgres database on db.<ref>.supabase.co with TLS; never echoes the URL."""
    validate_project_ref(project_ref)
    if not url:
        raise PlanError("SUPABASE_DB_URL is missing.")
    try:
        parts = urlsplit(url)
        port = parts.port
        username, password = parts.username, parts.password
    except ValueError:
        # Suppress the parser exception: its message may quote the URL.
        raise PlanError("SUPABASE_DB_URL is not a valid connection URL.") from None
    if parts.scheme not in ("postgres", "postgresql"):
        raise PlanError("SUPABASE_DB_URL must use the postgresql scheme.")
    if parts.fragment or "," in parts.netloc:
        raise PlanError("SUPABASE_DB_URL must target exactly one host.")
    if parts.hostname != f"db.{project_ref}.supabase.co":
        raise PlanError("SUPABASE_DB_URL host is not the direct database host of the project.")
    if port not in (None, DIRECT_PORT):
        raise PlanError("SUPABASE_DB_URL must use the direct database port 5432.")
    if username != DIRECT_USER or not password:
        raise PlanError("SUPABASE_DB_URL must authenticate as the postgres user with a password.")
    if parts.path != DIRECT_DATABASE_PATH:
        raise PlanError("SUPABASE_DB_URL must target the postgres database.")
    query = parse_qsl(parts.query, keep_blank_values=True)
    keys = [key for key, _ in query]
    if any(key not in ALLOWED_QUERY_KEYS for key in keys) or len(set(keys)) != len(keys):
        raise PlanError("SUPABASE_DB_URL contains unsupported or repeated query parameters.")
    if dict(query).get("sslmode") not in TLS_MODES:
        raise PlanError("SUPABASE_DB_URL must set sslmode=require or stronger.")


def validate_context(ref: str, input_sha: str, workflow_sha: str, checkout_sha: str, remote_main_sha: str) -> None:
    """Binds the run to the current head of refs/heads/main and rejects stale SHAs."""
    if ref != MAIN_REF:
        raise PlanError(f"Workflow must run from {MAIN_REF}.")
    if not SHA_PATTERN.match(input_sha or ""):
        raise PlanError("main_sha must be a full 40-character lowercase commit SHA.")
    if workflow_sha != input_sha:
        raise PlanError("The dispatched workflow commit does not match main_sha.")
    if checkout_sha != input_sha:
        raise PlanError("checked out commit does not match main_sha.")
    if remote_main_sha != input_sha:
        raise PlanError("main_sha is stale: refs/heads/main has moved.")


def _read(path: str) -> str:
    try:
        return Path(path).read_text(encoding="utf-8")
    except OSError as error:
        raise PlanError("Required CLI output file is missing or unreadable.") from error


def _run(args: argparse.Namespace, env: Mapping[str, str]) -> None:
    if args.command == "check-inputs":
        validate_project_ref(env.get("INPUT_PROJECT_REF", ""))
        if not SHA_PATTERN.match(env.get("INPUT_MAIN_SHA", "")):
            raise PlanError("main_sha must be a full 40-character lowercase commit SHA.")
        expected = parse_expected_versions(env.get("EXPECTED_PENDING_VERSIONS", ""))
        print(f"Inputs valid; approved pending versions: {', '.join(expected)}")
    elif args.command == "check-context":
        validate_context(env.get("GITHUB_REF", ""), env.get("INPUT_MAIN_SHA", ""), env.get("GITHUB_SHA", ""),
                         env.get("CHECKOUT_SHA", ""), env.get("REMOTE_MAIN_SHA", ""))
        print("Run is bound to the current head of refs/heads/main.")
    elif args.command == "check-db-url":
        project_ref = env.get("SUPABASE_PROJECT_REF", "")
        if not project_ref or env.get("INPUT_PROJECT_REF", "") != project_ref:
            raise PlanError("project_ref input does not match the environment SUPABASE_PROJECT_REF.")
        validate_db_url(env.get("SUPABASE_DB_URL", ""), project_ref)
        print("Database URL targets the approved direct host with TLS.")
    else:
        expected = parse_expected_versions(env.get("EXPECTED_PENDING_VERSIONS", ""))
        baseline = env.get("SUPABASE_APPROVED_BASELINE", "")
        if not VERSION_PATTERN.match(baseline):
            raise PlanError("SUPABASE_APPROVED_BASELINE is missing or invalid.")
        manifest = read_manifest(args.migrations_dir)
        history = parse_migration_list(_read(args.list_json))
        if args.command == "check-plan":
            files = check_plan(manifest, history, baseline, expected, parse_dry_run(_read(args.dry_run_log)))
            print("Validated plan; exactly these migrations will be applied:")
            for name in files:
                print(f"  {name}")
        else:
            verify_applied(manifest, history, baseline, expected)
            print("Remote history ends at the approved versions.")


def main(argv: Sequence[str] | None = None, env: Mapping[str, str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("check-inputs")
    commands.add_parser("check-context")
    commands.add_parser("check-db-url")
    for name in ("check-plan", "verify-applied"):
        command = commands.add_parser(name)
        command.add_argument("--migrations-dir", required=True)
        command.add_argument("--list-json", required=True)
        if name == "check-plan":
            command.add_argument("--dry-run-log", required=True)
    args = parser.parse_args(argv)
    try:
        _run(args, os.environ if env is None else env)
    except PlanError as error:
        print(f"::error::{error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
