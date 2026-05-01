# Zonar Hub API Agent Contract

This repository defines its AI operating model through `AGENTS.md`, `.github/`, `.claude/`, and `docs/architecture/`.

## Read order (MANDATORY)

1. `.github/copilot-instructions.md`
2. `.github/instructions/**/*.instructions.md`
3. `.github/skills/**/SKILL.md`
4. `.github/agents/*.agent.md`
5. `docs/architecture/**`
6. feature/module markdown close to the code

If any summary conflicts with this file, `AGENTS.md` takes precedence.

---

## Product context

- Product name: Zonar Hub API
- Domain: padel circuit management, player registration, rankings, draws, tournament lifecycle, complexes, courts, dashboards, live state, and integrations
- Consumer clients:
  - Angular frontend
  - future mobile apps
  - future third-party providers
- Platform:
  - ASP.NET Core HTTP API
  - OpenAPI-first documentation
  - bearer authentication
  - multilingual support
  - clean, reusable enterprise architecture

---

## Non-functional priorities

- simplicity
- maintainability
- observability
- performance
- reuse
- modularity
- interoperability
- future-proofing

---

## Global engineering rules (NON-NEGOTIABLE)

- Reuse before create
- Extend existing abstractions before introducing new parallel abstractions
- No duplicated utilities, helpers, mappers, validators, or adapters
- Respect standard .NET naming and folder conventions
- Separate domain, application, infrastructure, and presentation concerns
- Favor explicit contracts over implicit behavior
- Every externally exposed API must be documented
- Every date/time crossing boundaries must be handled in UTC
- No direct coupling between domain logic and external provider contracts
- Every external integration must go through an Anti-Corruption Layer
- All persistence must remain replaceable
- All user-facing text and API-facing messages must be localization-ready

---

## Target architecture

Prefer this evolution path unless there is a very strong reason not to:

- `src/ZonarHub.Api` → HTTP layer, OpenAPI, auth, localization, composition root
- `src/ZonarHub.Application` → use cases, orchestration, DTO contracts, validators
- `src/ZonarHub.Domain` → entities, value objects, domain services, rules
- `src/ZonarHub.Infrastructure` → persistence, external providers, HTTP clients, logging adapters, background integrations
- `tests/*` → unit, integration, contract tests

If the repository starts as a single API project, changes should still be implemented as if extraction into those layers is expected soon.

---

## API documentation rules (MANDATORY)

Every endpoint MUST be documented through OpenAPI/Swagger metadata.

Always provide:

- endpoint summary
- endpoint description when the behavior is non-trivial
- request schema documentation
- response schema documentation
- error response documentation
- authentication requirements
- tags/grouping
- versioning readiness

Public DTOs and relevant methods should include XML documentation summaries where this improves generated API documentation and maintainability.

Never ship an endpoint without accurate OpenAPI documentation.

---

## Multilingual rules (MANDATORY)

The API must be ready for localization and future expansion.

Supported locales for now:

- es
- en
- pt

Rules:

- do not hardcode outward-facing messages when a localized message is expected
- validation messages must be localizable
- error messages returned to clients must be localizable
- prepare the solution to add more locales later without breaking contracts
- keep stable machine-readable error codes separate from localized messages

---

## Authentication and authorization rules

- All endpoints are private by default unless explicitly opened
- Authentication uses bearer token
- Authorization must be centralized and policy-based when possible
- Avoid scattering permission logic across controllers/endpoints
- Keep identity and permission concerns encapsulated
- It must be easy to determine which roles/claims/policies can access which APIs

---

## Validation rules

- Prefer FluentValidation for request validation
- Validate at the application boundary, not deep in controllers
- Controllers/endpoints should receive already validated contracts
- Business rule validation must stay distinct from transport/input validation
- Validation errors must be consistent and localizable

---

## Logging and observability rules

The backend must emit logs for relevant execution flows and failures.

Requirements:

- use structured logging
- centralize logging setup through DI/modules
- log errors with enough diagnostic context
- avoid noisy duplicate logs
- propagate correlation/trace information when possible
- do not log secrets or sensitive values
- support operational diagnostics for support/admin/developers

Prefer pluggable logging through infrastructure/composition modules, not ad-hoc logger setup in random files.

---

## Dependency injection and composition rules

- Dependency registration must be encapsulated in extension modules per layer/capability
- Avoid an oversized Program.cs or Startup-like god file
- Use options/configuration classes per concern
- Bind configuration via DI and validate configuration where appropriate
- Infrastructure registration must stay outside domain logic

---

## External integration rules

All external systems (AI providers, TensorFlow services, payment systems, etc.) must be encapsulated.

Mandatory rules:

- use `IHttpClientFactory` or equivalent central factory-based client management
- never instantiate raw `HttpClient` ad-hoc per request flow
- define provider-facing contracts separately from internal contracts
- use Anti-Corruption Layer / adapters / mappers between external contracts and internal models
- provider changes must not break domain/application contracts directly
- resilience (timeouts/retries/circuit handling) must be centralized and explicit

---

## Persistence rules

Persistence must remain replaceable.

Rules:

- domain/application must not depend on a specific database vendor
- favor repositories, specifications/query abstractions, and persistence adapters
- Supabase is the current target, but changing to MySQL/PostgreSQL/other providers should be low-friction
- if Entity Framework is feasible, isolate provider-specific concerns
- if raw SQL/scripts are required, migrations/scripts must remain organized and traceable
- never leak vendor-specific details into business rules without an isolation layer

---

## Date and time rules

- Store and process canonical timestamps in UTC
- Centralize date/time formatting and conversion utilities
- Do not repeat date parsing/formatting logic across the codebase
- Expose outward-facing date handling through reusable components/services
- Time-zone presentation concerns must remain outside core domain logic unless they are business rules

---

## Documentation rules

Document when the code benefits from it, especially:

- public contracts
- endpoint behaviors
- non-obvious orchestration
- reusable infrastructure abstractions
- extension points
- complex business rules
- factory/strategy usage

Use XML summaries for public APIs and important reusable methods/classes where meaningful.

---

## Testing rules

Prefer a balanced automated testing strategy:

- unit tests for domain/application rules
- integration tests for persistence and external integration boundaries
- contract tests for public API behavior where helpful
- no empty or superficial tests

Test behavior and contracts, not incidental implementation details.

---

## Pattern usage guidance

Use patterns when they make complexity simpler, not for ceremony.

Likely valid patterns in this repository:

- Repository
- Factory
- Strategy
- Specification
- Anti-Corruption Layer
- Adapter
- Options
- Policy-based authorization
- Result/Error envelope patterns where appropriate

Avoid pattern cargo-culting.

---

## Testing and Coverage Policy (NON-NEGOTIABLE)

All backend components MUST have automated tests with 100% coverage for the affected unit under implementation or modification.

This is a strict requirement for all new or changed code.

This applies to:
- domain entities and value objects
- application services
- command/query handlers
- validators
- mappers
- repository abstractions and implementations
- infrastructure services
- authorization policies
- business rules
- reusable utility components

Mandatory rules:

- no new backend code without tests
- no modified backend code without updated tests
- all logical branches must be covered
- all success, failure, edge, and validation paths must be covered

Quality constraints:

- tests must validate behavior, not implementation details
- tests must be meaningful and maintainable
- tests must not exist only to artificially increase coverage
- avoid over-mocking that hides real behavior
- prefer real scenarios and realistic data flows when possible

Coverage interpretation:

- coverage must reflect real behavior validation
- unreachable or defensive code paths should be justified
- trivial getters/setters do not require artificial testing unless they contain logic

Enforcement:

- any code that does not meet these rules must be considered incomplete
- agents must generate or update tests as part of any implementation task

---

## Final rule

If there is doubt:

- choose explicitness over magic
- choose reuse over duplication
- choose modularity over convenience
- choose stable contracts over short-term shortcuts
