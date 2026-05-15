# Claude Local Bootstrap

Follow `/AGENTS.md` first.

This repository uses `.github/` as the canonical instruction system.
Do not create or follow a separate competing rule set inside `.claude/`.

Primary contract:

* `/AGENTS.md`
* `/.github/copilot-instructions.md`
* `/.github/instructions/**/*.instructions.md`
* `/.github/skills/**/SKILL.md`
* `/.github/agents/*.agent.md`
* `/docs/architecture/**`

Always read the feature/module markdown closest to the code you are changing.

When implementing backend features:

* inspect the consuming frontend contract if available
* design APIs for multiple clients, not only Angular
* keep all outward contracts documented through OpenAPI
* keep all outward messages localization-ready

## Persistence and database policy

Persistence must remain replaceable.

Application and domain layers must not depend directly on:

* Supabase
* PostgreSQL
* SQL vendor features
* ORM-specific implementations
* filesystem persistence
* cache engines
* external storage providers

Infrastructure owns provider-specific implementation details.

Always read:

* `/.github/instructions/08-persistence.instructions.md`

When the active provider has dedicated instructions, also read:

* `/.github/instructions/providers/**/*.instructions.md`

Current provider implementations are considered replaceable infrastructure details, not architectural dependencies.

All persistent schema changes must:

* remain traceable
* be represented as migration files when supported
* be verifiable locally
* avoid undocumented live-only changes

Do not invent migration filenames manually when the provider tooling supports generation commands.

## Migration policy

When working with schema changes:

* keep migrations cohesive and feature-oriented
* include indexes, constraints, policies, grants, triggers, and functions when part of the same feature
* verify migrations locally before considering the task complete
* keep migration history organized and reviewable

Migration history files are considered part of the application architecture and must remain readable and auditable.

## MCP and provider tooling policy

Claude may use configured MCP servers and provider tooling for:

* documentation lookup
* schema inspection
* query validation
* advisors
* local development database operations
* migration generation workflows

Claude must ask before:

* production database modifications
* destructive data operations
* irreversible schema removals
* security-sensitive production changes
* remote repository modifications

## Autonomous execution policy

You may execute builds, tests, package restores, migrations, local servers, linters, generators, file edits, and repository inspection commands without asking for permission.

You may freely:

* read/write/edit files
* run dotnet/npm/node/bash/powershell commands
* install dependencies
* execute local development servers
* run integration and unit tests
* create/delete temporary files
* inspect repository structure
* inspect logs
* run migration tooling
* execute local database scripts
* stage files with git add

You must ask for explicit approval before:

* git commit
* git push
* deleting branches
* force push
* modifying remote repository settings
* destructive production database operations
* executing irreversible destructive operations outside the repository

Assume the repository is a development sandbox unless explicitly stated otherwise.
