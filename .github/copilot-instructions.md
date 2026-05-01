# Zonar Hub API Copilot Instructions

This repository follows a deterministic backend operating model.

## Mandatory read order

1. `AGENTS.md`
2. `.github/instructions/**/*.instructions.md`
3. `.github/skills/**/SKILL.md`
4. `.github/agents/*.agent.md`
5. `docs/architecture/**`

## Global behavior

Always:

- preserve clean separation of concerns
- prioritize reusable abstractions
- document every public endpoint with OpenAPI metadata
- use localization-ready messages
- assume all timestamps are UTC
- route external integrations through Anti-Corruption Layers
- keep persistence replaceable
- prefer DI modules/extensions over centralized startup sprawl
- generate or update automated tests for every backend change
- satisfy the testing and coverage policy defined in `AGENTS.md`

## When implementing from frontend needs

If a frontend feature or component documentation exists:

1. inspect the consuming feature or component markdown
2. identify the exact contract required by the UI
3. design an API contract that is also suitable for non-Angular clients
4. implement the minimum stable backend contract
5. document the API thoroughly
6. validate auth, validation, logging, and localization implications

## Hard constraints

Never:

- create duplicate helper or mapper systems
- hardcode localized outward-facing messages when localization is expected
- instantiate ad-hoc `HttpClient`
- expose raw external provider contracts directly to domain/application
- bind business logic to a specific database vendor
- leave endpoints undocumented
- commit or push without explicit user permission (see `git-commit-workflow` skill)
- execute any Supabase write operation without explicit user authorization (see `supabase-write-guard` skill)