# Multi-tenant Architecture

ZonarHub is designed as a multi-tenant system.

The platform does not create one dedicated application instance or one dedicated database per customer.

Instead:

* multiple organizations share the same frontend deployment
* multiple organizations share the same backend/API deployment
* multiple organizations share the same persistence provider/database

Tenant isolation is achieved through organization-based ownership and filtering.

## Core model

Each user belongs to one or more organizations.

Each organization owns its own operational data, such as:

* users
* players
* tournaments
* registrations
* complexes
* courts
* rankings
* matches
* settings
* notifications
* sports
* billing
* etc

Most tenant-owned records must include an `OrganizationId` or equivalent tenant identifier.

## Architecture rule

Tenant isolation is both:

* a business boundary
* a security boundary

Application and infrastructure code must never assume global access to tenant-owned data unless the use case explicitly requires platform-level administration.

Repositories, queries, commands, API endpoints, and services must remain tenant-aware.

## Persistence rule

When using database persistence providers, tenant-owned tables must include tenant identification.

Possible enforcement strategies include:

* application-level filtering
* repository-level filtering
* database constraints
* Row Level Security (RLS)
* provider-specific policies

The current persistence provider is Supabase/PostgreSQL, but the tenant model must remain provider-agnostic.

## API rule

API endpoints must resolve the current organization context before accessing tenant-owned data.

The organization context may come from:

* authenticated user claims
* selected organization
* backend session state
* route context
* explicit platform-admin flows

Endpoints must never trust arbitrary organization IDs sent by the client without authorization validation.

## Frontend rule

The frontend may render organization-specific UI based on the current organization context.

However:

* frontend filtering is not a security boundary
* tenant isolation must always be enforced by the backend

Frontend implementations should avoid:

* hardcoded organization behavior
* organization-specific forks
* duplicated tenant rules already enforced by the backend

## Hosting rule

The expected deployment model is shared-instance hosting.

Examples:

* one frontend serving many organizations
* one backend/API serving many organizations
* one database serving many organizations

Cloudflare, Hostinger, Supabase, PostgreSQL, or any other hosting/provider technology are deployment details, not architectural dependencies.

## Agent rule

When implementing any feature involving tenant-owned data, always verify:

* which organization owns the data
* how the current organization is resolved
* whether the user has access to the organization
* whether queries are correctly scoped
* whether repository filtering is enforced
* whether API contracts expose organization context appropriately
* whether backend authorization protects tenant boundaries
