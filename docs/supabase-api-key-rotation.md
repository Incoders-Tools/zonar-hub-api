# Supabase API key rotation: move the API to a new `sb_secret_` key

**Resource:** Supabase project `zonar-hub`, project ref **`bwaskyulujzbnfrynvdd`**.
**Dashboard location:** Project Settings → **API Keys**.

> **Current status (2026-10-07):** Compatible code (`4244089`) is on `dev` through merged PRs #3–#9; the final workflow/evidence slice awaits review and merge into `dev`. `main` remains at `62173b8`, unpromoted, and no hosted API deployment or new database write has occurred. GitHub environment `supabase-production` is `main`-only with its secret and variables present; `main` requires three CI checks, and `promotion-policy` becomes selectable only after a future `dev`→`main` PR. The user created a new `sb_secret_` key, Supabase accepted it (HTTP 200 with a server-like User-Agent), and the local API started with it from `Supabase__Key`. The user **reported** disabling the legacy keys in Dashboard (change 4); this has not been independently verified. No hosted API exists yet. The steps below remain the procedure for any future host or key rotation.

The legacy `service_role` key for this project was committed to this repository and must be treated as compromised. The API now supports new `sb_secret_…` keys (sent as `apikey` only, never as `Authorization: Bearer`), and the tracked `ZonarHub.ApiService/appsettings.Development.json` keeps `Supabase:Key` empty. Every environment must supply the key at runtime through `Supabase__Key`.

Official references:

- [Supabase API keys](https://supabase.com/docs/guides/getting-started/api-keys)
- [Migrating to new API keys](https://supabase.com/docs/guides/getting-started/migrating-to-new-api-keys)

> **This guide is not authorization.** Each change below needs its own explicit approval from the responsible owner at the time it is performed.

## Four separate changes

| # | Change | Who/where | Requires its own approval |
|---|--------|-----------|---------------------------|
| 1 | Use compatible API code locally; deliver it by commit, push, PR, merge and deployment only when needed for another host | Local source tree now; Git repository and future API host later | Local verification needs no delivery; **separate approval for each later GitHub operation and deployment** |
| 2 | Dashboard: create a new `sb_secret_` key | Supabase Dashboard, project `bwaskyulujzbnfrynvdd` | Yes |
| 3 | Runtime: set `Supabase__Key` on the hosted API and restart | Hosting platform secret store | Yes |
| 4 | Dashboard: disable legacy API keys | Supabase Dashboard, project `bwaskyulujzbnfrynvdd` | Yes, after all consumers are confirmed migrated |

Approving one change does not approve the next; the later GitHub actions within change 1 also require individual approvals. Each push, PR and `dev` merge of change 1 so far was individually authorized; promotion to `main` and any deployment still need their own approval. **For today's local-only API, run the updated source directly; no Git delivery is needed for a local key check.** A future hosted API must first deploy compatible code before switching its runtime key, because older builds send the key as a Bearer token. No hosted API or secret-store owner has been identified.

## Quick path

1. **Use a compatible build (change 1).** Today, run the updated API source locally as described below; no Git or deployment step is necessary. If a hosted API is added later, separately authorize each GitHub/deployment action and deploy compatible code before switching its key.
2. **Create the key (change 2).** In Project Settings → API Keys, create a new secret key (`sb_secret_…`) with a descriptive name, such as `zonar-hub-api`. Copy it directly into the hosting platform's secret store only at the separately authorized runtime step. Do not paste it into chat, tickets, commits, logs, or files in this repository.
3. **Set the runtime secret (change 3).** On the hosted API, set the environment variable or secret-store entry `Supabase__Key` to the new key, using the platform's own documented mechanism. Then **restart** the API. The named HTTP client reads `IOptions<SupabaseOptions>` once, so a running process will not pick up the change.
4. **Smoke test (read-only).** Sign in through the normal API authentication flow to get a fresh user token, then call one read-only endpoint (a `GET` list or detail) and confirm a `2xx` response with expected data. Do not issue writes as part of the smoke test.
5. **Inventory consumers before disabling legacy keys.** See the checklist below.
6. **Disable legacy keys (change 4).** In Project Settings → API Keys, disable the legacy `anon`/`service_role` keys. Legacy keys **remain active until this separate step** is performed; creating a new key does not revoke them.

## Details

| Topic | Decision |
|-------|----------|
| Key type | Server-side `sb_secret_…` key only. Startup validation rejects `sb_publishable_…` keys, empty keys, and malformed values. |
| Header behavior | `sb_secret_` keys: `apikey` header only. Legacy JWT keys: `apikey` plus `Authorization: Bearer` (temporary overlap only). |
| Configuration source | `Supabase__Key` environment/secret-store override. It takes precedence over JSON through standard ASP.NET Core configuration. The tracked JSON value stays empty, so a missing override fails startup. |
| `Supabase:Url` | Unchanged. Same project URL. |
| Local development | Developers set `Supabase__Key` in a local environment variable or an explicitly configured external secret store. This API project has no `UserSecretsId`; do not assume .NET user secrets work without configuring them. Never write the key to a tracked file. |
| Git history | Blanking the tracked value does not remove the old key from Git history. The real remediation is disabling legacy keys (change 4). |

## Local-only API run (current setup)

The API currently runs on a developer machine; Azure, Railway, and AWS are possibilities, **not deployed hosts**. The API project has no `UserSecretsId`. `ZonarHub.AppHost/AppHost.cs` does not explicitly pass `Supabase__Key` to `apiservice`, so do not assume Aspire inherits the variable. For the first local check, run the API project directly from this repository in a fresh PowerShell session, after the separately approved Dashboard key-creation step:

```powershell
$secureKey = Read-Host 'New Supabase secret API key' -AsSecureString
$env:Supabase__Key = [System.Net.NetworkCredential]::new('', $secureKey).Password
$secureKey.Dispose()
dotnet run --project ZonarHub.ApiService
# After stopping the API with Ctrl+C:
Remove-Item Env:Supabase__Key
```

Enter the new key only into the no-echo prompt; never paste it into commands, chat, logs, or files in this repository. A process environment variable is temporary but **not an encrypted secret store**: it is available to child processes and may be inspectable by software running as the same user. Do not use `setx`, a persistent user environment variable, or a CLI argument containing the key. The configured project URL already exists in the tracked development JSON; do not override it unless the project changes.

`GET /health` and `GET /` check local startup only; they **do not prove Supabase accepted the key**. After local startup, use a fresh normal user sign-in and a read-only authenticated API `GET` that queries Supabase to check actual access. If the API fails startup or that `GET` fails, stop: do not disable legacy keys. To use AppHost later, verify secret propagation or explicitly configure it in a separate reviewed change; restart the whole AppHost after a key change.

## Before disabling legacy keys

- [ ] Every running API (currently local only) uses a build that includes `sb_secret_` support and has passed the smoke test with the new key. Any future hosted API requires its own deployment and runtime verification.
- [ ] Every developer machine and CI job that calls Supabase uses a new key.
- [ ] Every external client has been checked, including any frontend, mobile app, or third-party integration that uses the legacy **`anon`** (publishable) key. Such clients need a new `sb_publishable_…` key before legacy keys are disabled, or they will stop working.
- [ ] Any Edge Functions, scheduled jobs, or webhooks that use legacy keys have been identified and migrated.
- [ ] A project administrator has explicitly approved change 4.

## Failure handling

- **Startup fails after setting `Supabase__Key`:** the value is missing, padded, or not a `sb_secret_` key. Validation messages never include the key. Correct the value in the secret store and restart.
- **Smoke test fails with `401`/`403` from Supabase:** confirm that the deployed build includes `sb_secret_` support and that the restart happened. Do not disable legacy keys.
- **A step fails:** stop, preserve the currently working runtime, and escalate. Do not continue to the next change.
- **No rollback to the compromised key.** Never reuse, test, or restore the previously committed legacy key. If the new key itself is suspected to be exposed, create another new secret key, update the runtime secret, and delete the exposed one.

## Prohibited actions

- Testing the old key to check whether it still works.
- Printing, decoding, hashing, or logging any key value.
- Storing any key in tracked files, documentation, samples, or issue trackers.

## Next step

The database migration work in `odd/tasks/supabase-api-key-remediation.md` (AK-4) is complete: migrations #19 and #20 were each applied once under separate approvals; the shared history was last independently confirmed at 20/20 (see `odd/tasks/supabase-schema-reconciliation.md`). Never replay #19 or #20. Next: review and merge the final `dev` workflow/evidence slice. Promotion of `dev` to `main`, any hosted API deployment and independent verification of the legacy-key disablement each still need their own approval.
