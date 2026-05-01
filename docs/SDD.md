# Zonar Hub – Software Design Document (SDD)

## 1. Architecture

* Hexagonal Architecture
* Layers:

  * Domain
  * Application
  * Infrastructure

---

## 2. Core Principles

* Domain is independent of infrastructure
* Persistence is abstracted via repositories
* Providers selected via configuration

---

## 3. Key Abstractions

### Repository Pattern

Example:

* ISocialNetworkRepository
* IUserRepository
* ITournamentRepository

Responsibilities:

* Data access abstraction
* No knowledge of underlying storage

---

## 4. Persistence Strategy

* Default: In-memory / cache
* Optional: Supabase (PostgreSQL)

Selection via configuration:

```json
Persistence:Provider = "Memory" | "Supabase"
```

---

## 5. Integrations

* MercadoLibre (external API)
* WhatsApp via n8n (future)
* Supabase (optional persistence)

---

## 6. Application Flow

1. User registers
2. Player joins tournament
3. Defines availability
4. System generates groups
5. System schedules matches

---

## 7. Testing Strategy

* Unit tests (domain logic)
* Integration tests (providers)
* Mock external APIs

---

## 8. Extensibility

* Strategy pattern for tournament logic
* Pluggable providers
* External service abstraction

---
