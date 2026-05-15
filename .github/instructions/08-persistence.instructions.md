# Persistence Instructions

Persistence must remain replaceable.

Business and application layers must not depend directly on:

* Supabase
* PostgreSQL
* SQL vendor-specific features
* ORM implementations
* filesystem persistence
* cache engines
* external storage providers

Persistence concerns belong to infrastructure.

## Rules

* business logic must not know the persistence vendor
* repositories and persistence adapters are preferred
* infrastructure owns provider-specific implementations
* isolate ORM/provider-specific code
* avoid leaking provider-specific types into application contracts
* persistence provider selection should be configurable
* migrations and persistence scripts must remain organized and traceable
* persistence changes must be reviewable and reproducible
* schema evolution must be represented through migrations when supported by the provider

## Provider policy

Supabase is the current persistence provider target.

However:

* Supabase must not become a hard architectural dependency
* provider-specific logic must remain isolated
* provider replacement must remain possible without affecting domain/application layers

Possible future providers may include:

* PostgreSQL
* SQL Server
* SQLite
* Redis
* FileSystem
* InMemory
* Supabase
* REST-backed persistence
* event sourcing implementations

## Migration policy

Migration files are considered part of the architecture.

Rules:

* keep migrations cohesive and feature-oriented
* keep migrations readable and auditable
* include indexes, constraints, policies, triggers, and grants when part of the same feature
* avoid undocumented live-only schema changes
* verify migrations locally before considering the task complete

## Provider tooling

Provider-specific tooling and MCP integrations may be used for:

* schema inspection
* query validation
* migration workflows
* advisors and diagnostics
* local development database operations

Provider tooling must not leak architectural coupling into application or domain layers.
