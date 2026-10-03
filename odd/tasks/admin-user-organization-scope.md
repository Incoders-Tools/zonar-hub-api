# Admin user organization scope (API)

## Goal and decision
On the Users page, default to the selected real organization; allow an explicit All organizations mode only for system admins. Never represent All as a Zonar Hub organization. A test organization is separate and deferred. Implement backend authorization and filtering before pagination so the UI cannot present a misleading first-200 subset.

## Boundaries
- API target starts on `main`; create `feat/admin-user-organization-scope` before its first work-unit commit. Do not push, deploy a migration, or alter production data without a separate decision.
- Preserve tenant-admin access to unassigned users of their tenant for the existing assignment workflow, but a system admin in selected-organization mode sees only associated users. Document the exception in the response/UI. Validate selected org server-side from actual organization and caller tenant; never trust `X-Organization-Id` as authorization.
- Global mode must be explicit and forbidden to non-system admins; a missing org must not silently become global. Support both primary and secondary organization assignments; apply filtering before `LIMIT/OFFSET` and compute accurate total count.
- Existing dashboard `UserQuery` uses global/tenant semantics and must retain its behavior. Preserve existing create/update and other endpoint contracts. Do not enable or implement impersonation within this feature.
- Strict test-first for behavior; run `dotnet test ZonarHub.Tests/ZonarHub.Tests.csproj --artifacts-path C:/temp/zonar-hub-api-users-scope-build` and `dotnet build ZonarHub.slnx --artifacts-path C:/temp/zonar-hub-api-users-scope-build` while a local API holds default bin/obj output locks. Report that the plain runner failed to build when PID 3548 held DLLs; never stop the developer's API just to make a test path work. SQL migration needs a disposable Postgres harness or an explicit skipped-live note; neither local tests nor a migration file proves live installation.

## Tasks
- [ ] AUS-API-1 Add a minimal explicit organization/all query contract, validate scope and tenant authorization in handler, and cover sysadmin/org, tenant-admin/org, forbidden global, missing/foreign/inactive org and unassigned tenant users in unit tests and in-memory repository. Keep each commit reviewable.
- [ ] AUS-API-2 Implement real Postgres organization membership filter (primary OR assigned) with accurate count before paging, service-role-only grants, focused SQL regressions, Supabase adapter tests, and full API checks; commit migration plus behavior together. Never apply to live DB here.
- [ ] AUS-API-3 Align endpoint OpenAPI/problem responses and response mapping/assignment visibility with the scoped contract; test pagination and multi-org users. Re-run full suite/build and commit if separate from prior units.

## Evidence and blockers
- Initial code inspection: existing `GET /api/admin/users` passes no organization filter and sysadmin receives all users; PostgREST adapter filters only tenant and paginates before joining assignments. Frontend currently requests only 200 users. No live API response or migration status known.
- AUS-API-1 RED: initial new tests did not compile, then 18 failed/3 passed before handler logic. GREEN: 21/21 scope cases; legacy tenant test was updated to pass an active organization without weakening its assertions. Endpoint-level regression tests added for 403/400/200 and different-org-only assignment. Final writer and independent verifier ran 242/242 tests and solution build with isolated artifacts; 4 NU1903 warnings are pre-existing advisory noise. `git diff --check` passed. Plain default-output test build hit DLL locks from the developer's running API PID 3548; no process was stopped, and the isolated runner is canonical while the API runs. Production adapter intentionally refuses scoped requests until AUS-API-2: do not deploy this feature branch partially. Split the ~684 authored source/test lines into a fail-closed core slice (~558, >400 because ~406 lines are behavior tests) plus a small endpoint/HTTP mapping slice (~124); never include unrelated `.gitignore`. Commit identities pending.
- Remaining tasks, verification, skips and rollback boundary: pending.
- Mirror key: `odd/admin-user-organization-scope/tasks` (Engram attempted where available).
