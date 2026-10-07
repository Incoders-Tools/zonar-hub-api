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
  check-ci-promotion   prove main is a green CI run of a merged dev to main PR
  check-main-head      re-bind the CI head SHA to the checkout and current main head
  check-auto-config    validate project ref, baseline and SUPABASE_DB_URL (no inputs)
  auto-plan            derive pending migrations and write GITHUB_OUTPUT values
  verify-auto-applied  confirm the remote history equals the full local manifest

The last five back the automatic deploy workflow, which runs after every green
CI push to main. The first five are not referenced by any workflow.
"""

from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
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
# Public Supabase Root 2021 CA committed for the pooler. The pin is the DER digest so
# checkout line-ending conversion cannot change it; PGSSLROOTCERT must name this file.
POOLER_CA_RELATIVE_PATH = "supabase/certs/prod-ca-2021.crt"
POOLER_CA_DER_SHA256 = "807025ad50d4ed219d2c9c7d299c004f824eb00cf7f65afef607d07b72e6cafa"
PEM_CERTIFICATE = re.compile(r"-----BEGIN CERTIFICATE-----([A-Za-z0-9+/=\s]+)-----END CERTIFICATE-----\s*")
# The only workflow whose push run on main may authorize a production apply.
CI_WORKFLOW_PATH = ".github/workflows/ci.yml"
REPOSITORY_PATTERN = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
# Step output names written by `auto-plan`; an existing definition would make them ambiguous.
AUTO_PLAN_OUTPUTS = ("pending", "pending_count")
OUTPUT_NAME = re.compile(r"^([^=<]*)(?:=|<<)")


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


def validate_pinned_ca(env: Mapping[str, str]) -> None:
    """Requires PGSSLROOTCERT to name the committed CA under GITHUB_WORKSPACE with the pinned digest.

    The file must hold exactly one PEM certificate, so no extra trust anchor or key
    can ride along. Messages never echo paths or file content.
    """
    workspace = env.get("GITHUB_WORKSPACE", "")
    if not workspace or any(ord(char) < 32 for char in workspace) or not os.path.isabs(workspace):
        raise PlanError("GITHUB_WORKSPACE must be an absolute path without control characters.")
    expected = f"{workspace}/{POOLER_CA_RELATIVE_PATH}"
    if env.get("PGSSLROOTCERT", "") != expected:
        raise PlanError("PGSSLROOTCERT must point to the pinned CA certificate in the workspace.")
    path = Path(expected)
    if path.is_symlink() or not path.is_file():
        raise PlanError("Pinned CA certificate file is missing or not a regular file.")
    try:
        text = path.read_text(encoding="ascii")
    except (OSError, ValueError):
        raise PlanError("Pinned CA certificate file is unreadable.") from None
    match = PEM_CERTIFICATE.fullmatch(text)
    try:
        der = base64.b64decode("".join(match.group(1).split()), validate=True) if match else b""
    except binascii.Error:
        der = b""
    if not der:
        raise PlanError("Pinned CA certificate file must contain exactly one PEM certificate.")
    if hashlib.sha256(der).hexdigest() != POOLER_CA_DER_SHA256:
        raise PlanError("Pinned CA certificate does not match the approved Supabase Root 2021 CA.")


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


def _field(document: object, *path: str) -> object:
    """Walks nested mappings; any missing or non-mapping step yields None."""
    for key in path:
        if not isinstance(document, dict):
            return None
        document = document.get(key)
    return document


def _text(document: object, *path: str) -> str:
    """Returns a nested string field, rejecting any other shape without echoing it."""
    value = _field(document, *path)
    if not isinstance(value, str):
        raise PlanError(f"Field {'.'.join(path)} is missing or malformed.")
    return value


def _bind_to_main_head(sha: str, checkout_sha: str, remote_main_sha: str) -> None:
    """Requires a well-formed CI head SHA that is checked out and still the head of main."""
    if not SHA_PATTERN.fullmatch(sha):
        raise PlanError("CI workflow run head SHA is malformed.")
    if checkout_sha != sha:
        raise PlanError("checked out commit does not match the CI head SHA.")
    if remote_main_sha != sha:
        raise PlanError("CI head SHA is stale: refs/heads/main has moved.")


def validate_main_head(event: object, checkout_sha: str, remote_main_sha: str) -> str:
    """Returns the CI head SHA of a `workflow_run` event while it is still the head of main.

    Deploy steps call this after the promotion gate and again right before applying, so
    a merge that lands on main in between stops the run. Messages never echo values.
    """
    sha = _text(event, "workflow_run", "head_sha")
    _bind_to_main_head(sha, checkout_sha, remote_main_sha)
    return sha


def validate_ci_promotion(
    run: object,
    pulls: object,
    expected_repo: str,
    checkout_sha: str,
    remote_main_sha: str,
) -> str:
    """Returns the main SHA when a green CI push run proves a merged dev to main promotion.

    `run` is the GitHub `workflow_run` object and `pulls` the pull requests GitHub
    associates with its head commit; fetching them is the caller's job. Exactly one
    merged PR must have that commit as its merge commit, and it must come from the
    same-repository `dev` branch. Direct pushes with no merged PR association, PR-only
    runs, forks, failed or unfinished runs and stale SHAs all fail. GitHub can still
    mark an open dev PR as merged when its head is fast-forward pushed to main, so
    branch protection on main is required to exclude that bypass. Messages never echo
    payload values.
    """
    if not isinstance(expected_repo, str) or not REPOSITORY_PATTERN.match(expected_repo):
        raise PlanError("Expected repository must be an owner/name pair.")
    if not isinstance(run, dict):
        raise PlanError("CI workflow run payload is malformed.")
    if (_text(run, "repository", "full_name") != expected_repo
            or _field(run, "head_repository", "full_name") != expected_repo):
        raise PlanError("CI workflow run does not belong to the expected repository.")
    if _text(run, "path") != CI_WORKFLOW_PATH:
        raise PlanError("Workflow run is not the CI workflow.")
    if _text(run, "event") != "push":
        raise PlanError("CI workflow run was not caused by a push.")
    if _text(run, "head_branch") != "main":
        raise PlanError("CI workflow run did not test main.")
    if _text(run, "status") != "completed":
        raise PlanError("CI workflow run is not completed.")
    if _field(run, "conclusion") != "success":
        raise PlanError("CI workflow run is not successful.")
    sha = _text(run, "head_sha")
    _bind_to_main_head(sha, checkout_sha, remote_main_sha)

    if not isinstance(pulls, list):
        raise PlanError("Associated pull request list is malformed.")
    merged = []
    for pr in pulls:
        merged_at = _field(pr, "merged_at")
        if not isinstance(pr, dict) or not (merged_at is None
                                            or isinstance(merged_at, str) and merged_at.strip()):
            raise PlanError("Associated pull request entry is malformed.")
        for path in (("base", "ref"), ("head", "ref")):
            _text(pr, *path)
        if merged_at is not None and _text(pr, "merge_commit_sha") == sha:
            merged.append(pr)
    if not merged:
        raise PlanError("main commit has no merged pull request; direct pushes are not deployable.")
    if len(merged) > 1:
        raise PlanError("main commit is the merge commit of more than one pull request.")
    (pr,) = merged
    if (_text(pr, "base", "ref") != "main" or _text(pr, "head", "ref") != "dev"
            or _field(pr, "base", "repo", "full_name") != expected_repo
            or _field(pr, "head", "repo", "full_name") != expected_repo):
        raise PlanError("Merged pull request is not a same-repository dev to main promotion.")
    return sha


def _read(path: str) -> str:
    try:
        return Path(path).read_text(encoding="utf-8")
    except OSError as error:
        raise PlanError("Required CLI output file is missing or unreadable.") from error


def _read_json(path: str, label: str) -> object:
    """Parses a trusted JSON file; the error never quotes its content."""
    try:
        return json.loads(_read(path))
    except ValueError:
        raise PlanError(f"{label} file is not valid JSON.") from None


def _read_baseline(env: Mapping[str, str]) -> str:
    baseline = env.get("SUPABASE_APPROVED_BASELINE", "")
    if not VERSION_PATTERN.match(baseline):
        raise PlanError("SUPABASE_APPROVED_BASELINE is missing or invalid.")
    return baseline


def _trusted_output_file(value: str) -> Path:
    """Returns GITHUB_OUTPUT only when it is an absolute existing file safe to append to.

    Control characters in the path, a missing or non-regular file, a last line without
    a newline, or an existing definition of an auto-plan output all fail closed.
    """
    if not value or any(ord(char) < 32 for char in value) or not os.path.isabs(value):
        raise PlanError("GITHUB_OUTPUT must be an absolute path without control characters.")
    path = Path(value)
    if not path.is_file():
        raise PlanError("GITHUB_OUTPUT does not point to an existing file.")
    try:
        existing = path.read_text(encoding="utf-8")
    except (OSError, ValueError):
        raise PlanError("GITHUB_OUTPUT file is unreadable.") from None
    if existing and not existing.endswith("\n"):
        raise PlanError("GITHUB_OUTPUT file does not end with a newline.")
    for line in existing.splitlines():
        match = OUTPUT_NAME.match(line)
        if match and match.group(1) in AUTO_PLAN_OUTPUTS:
            raise PlanError("GITHUB_OUTPUT file already defines an auto-plan output.")
    return path


def _write_outputs(path: Path, text: str) -> None:
    """Appends every output in a single write so readers never see a partial set."""
    with path.open("a", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def _run_auto(args: argparse.Namespace, env: Mapping[str, str]) -> None:
    if args.command == "check-ci-promotion":
        validate_ci_promotion(_read_json(args.run_json, "CI workflow run"),
                              _read_json(args.pulls_json, "Associated pull request"),
                              env.get("GITHUB_REPOSITORY", ""), env.get("CHECKOUT_SHA", ""),
                              env.get("REMOTE_MAIN_SHA", ""))
        print("CI promotion verified: main is a green CI push of a merged dev to main PR.")
        return
    if args.command == "check-main-head":
        validate_main_head(_read_json(args.event_json, "Workflow event"),
                           env.get("CHECKOUT_SHA", ""), env.get("REMOTE_MAIN_SHA", ""))
        print("Run is bound to the CI head SHA, which is the current head of refs/heads/main.")
        return
    if args.command == "check-auto-config":
        _read_baseline(env)
        validate_db_url(env.get("SUPABASE_DB_URL", ""), env.get("SUPABASE_PROJECT_REF", ""))
        print("Environment configuration valid; database URL targets the approved direct host with TLS.")
        return
    # Output trust is settled before any CLI file is read so a failure can never leave
    # a half-validated plan behind; outputs are written only after full validation.
    output = _trusted_output_file(env.get("GITHUB_OUTPUT", "")) if args.command == "auto-plan" else None
    baseline = _read_baseline(env)
    manifest = read_manifest(args.migrations_dir)
    history = parse_migration_list(_read(args.list_json))
    if output is None:
        verify_auto_applied(manifest, history, baseline)
        print("Remote history equals the complete local manifest.")
        return
    files = check_auto_plan(manifest, history, baseline, _read(args.dry_run_log))
    try:
        _write_outputs(output, f"pending={'true' if files else 'false'}\npending_count={len(files)}\n")
    except OSError:
        raise PlanError("GITHUB_OUTPUT could not be written.") from None
    if files:
        print("Validated automatic plan; exactly these migrations will be applied:")
        for name in files:
            print(f"  {name}")
    else:
        print("Validated automatic plan; no migrations are pending.")


def _run(args: argparse.Namespace, env: Mapping[str, str]) -> None:
    if args.command in ("check-ci-promotion", "check-main-head", "check-auto-config", "auto-plan",
                        "verify-auto-applied"):
        _run_auto(args, env)
    elif args.command == "check-inputs":
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
        baseline = _read_baseline(env)
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
    promotion = commands.add_parser("check-ci-promotion")
    promotion.add_argument("--run-json", required=True)
    promotion.add_argument("--pulls-json", required=True)
    commands.add_parser("check-main-head").add_argument("--event-json", required=True)
    commands.add_parser("check-auto-config")
    for name in ("check-plan", "verify-applied", "auto-plan", "verify-auto-applied"):
        command = commands.add_parser(name)
        command.add_argument("--migrations-dir", required=True)
        command.add_argument("--list-json", required=True)
        if name in ("check-plan", "auto-plan"):
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
