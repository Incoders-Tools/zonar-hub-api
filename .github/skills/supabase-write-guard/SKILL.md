# Skill: Supabase Write Guard

Use this skill whenever interacting with the Supabase project (via MCP, CLI, REST API, or SQL).

Rules:

- read operations (SELECT, GET, list tables, inspect schema, view logs) may run freely without asking
- every write operation requires explicit user authorization before execution — no exceptions
- write operations that require authorization include:
  - migrations: `supabase db push`, `supabase migration up`, `supabase db reset`, any DDL applied remotely
  - SQL writes: INSERT, UPDATE, DELETE, TRUNCATE, UPSERT
  - schema changes: CREATE TABLE, ALTER TABLE, DROP TABLE, CREATE INDEX, CREATE FUNCTION, CREATE POLICY, CREATE TRIGGER, and equivalents
  - Supabase CLI commands that modify remote state: `supabase secrets set`, `supabase functions deploy`, `supabase storage`
  - RPC calls or Edge Functions that mutate data
- before any authorized write: show exactly what SQL or command will be executed and ask "¿Autorizás esta operación?" before proceeding
- if a migration file is created locally but not yet pushed, that is safe — only remote execution requires authorization
- never run write operations speculatively, as side effects, or as cleanup steps without asking first
- if multiple write operations are needed in sequence, list all of them upfront and get a single authorization for the full set
