# Persistence Instructions

Persistence must be replaceable.

Rules:

- business logic must not know the DB vendor
- repositories and persistence adapters are preferred
- isolate ORM/provider-specific code
- keep migrations/scripts organized and traceable
- Supabase is current target, but must not become a hard architectural dependency
