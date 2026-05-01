# Zonar Hub API – Copilot Instructions

This repository follows a deterministic backend operating model.

---

## Source of truth

This project is driven by:

* Product definition: `/docs/PRD.md`
* System design: `/docs/SDD.md`

Always:

* use PRD to understand WHAT should be built
* use SDD to understand HOW it should be implemented
* do not invent business rules outside PRD
* do not introduce architectural decisions outside SDD

---

## Mandatory read order

1. `AGENTS.md`
2. `.github/instructions/**/*.instructions.md`
3. `.github/skills/**/SKILL.md`
4. `.github/agents/*.agent.md`
5. `docs/PRD.md`
6. `docs/SDD.md`
7. `docs/architecture/**`

---

## Global behavior

Always:

* preserve clean separation of concerns
* follow hexagonal architecture principles
* prioritize reusable abstractions
* document every public endpoint with OpenAPI metadata
* use localization-ready messages
* assume all timestamps are UTC
* route external integrations through Anti-Corruption Layers
* keep persistence replaceable via provider abstraction
* prefer DI modules/extensions over centralized startup sprawl
* generate or update automated tests for every backend change
* satisfy the testing and coverage policy defined in `AGENTS.md`
* treat PRD as the source of business truth
* treat SDD as the source of architectural constraints

---

## Architecture rules

* Domain layer must not depend on infrastructure
* Application layer orchestrates use cases only
* Infrastructure implements external concerns (DB, APIs, etc.)
* All persistence must go through repository abstractions
* Never bypass repository interfaces defined in Application
* Persistence provider must be selected by configuration only
* Do not bind logic to a specific database vendor
* All external APIs must be accessed via Anti-Corruption Layers

---

## Persistence strategy

* Default provider: in-memory or cache
* Optional provider: Supabase (PostgreSQL)

Rules:

* never assume a specific provider
* never introduce provider-specific logic in Domain/Application
* all persistence decisions must follow abstractions defined in SDD

---

## When implementing from frontend needs

If a frontend feature or component documentation exists:

1. inspect the consuming feature or component markdown
2. identify the exact contract required by the UI
3. design an API contract that is also suitable for non-specific clients
4. implement the minimum stable backend contract
5. document the API thoroughly (OpenAPI)
6. validate:

   * authentication
   * validation
   * logging
   * localization

---

## Testing rules

* every new feature must include tests
* domain logic → unit tests
* infrastructure/providers → integration tests
* external APIs must be mocked unless explicitly required
* tests must be deterministic

---

## Hard constraints

Never:

* create duplicate helper or mapper systems
* hardcode localized outward-facing messages when localization is expected
* instantiate ad-hoc `HttpClient` (use HttpClientFactory)
* expose raw external provider contracts to Domain/Application
* bind business logic to a specific persistence technology
* leave endpoints undocumented
* introduce hidden side effects
* commit or push without explicit user permission
* execute any Supabase write operation without explicit user authorization

---

## Commit policy

Before any commit:

1. show the full diff
2. wait for explicit user confirmation
3. only proceed if approved

---

## Supabase safety rules

* treat Supabase as an external provider
* never perform write operations without explicit approval
* never rely on local CLI state (.temp, etc.)
* use migrations and declarative schema only

---

## Development philosophy

* favor clarity over cleverness
* prefer explicit contracts over implicit behavior
* design for extensibility (providers, strategies, integrations)
* keep the system modular and testable
* avoid premature optimization

---

## Expected output quality

All generated code must:

* compile
* follow project architecture
* include necessary abstractions
* include tests when applicable
* be production-ready (no placeholders or TODOs unless explicitly requested)

---
