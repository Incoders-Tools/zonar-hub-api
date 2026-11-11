# Admin players, eligibility profiles and registrations API

Backoffice side of the registrations feature (S3, S4; D2, D3, D5 of `zonar-hub/odd/tasks/registrations.md`).
Code: `ZonarHub.ApiService/Endpoints/Admin/Circuit/{Players,EligibilityProfiles,Tournaments}/`, `ZonarHub.Application/Features/{Players,EligibilityProfiles,TournamentRegistrations}/`, `ZonarHub.Domain/Registrations/RegistrationEligibility.cs`.
Persistence and the SQL functions are those of migration `20261110100000_tournament_registrations`.

## Access

- Every route requires the `AdminOrAbove` policy.
- Organization routes (`/api/admin/organizations/{organizationId}/…`) apply the tournament tenant rule to the organization. System administrators may manage any organization; other callers only an organization of their own tenant. Unknown or foreign organizations return `403` (`tournaments.forbidden`).
- Tournament routes (`/api/admin/tournaments/{tournamentId}/registrations…`) return `404` (`tournaments.not_found`) for an unknown tournament and `403` (`tournaments.forbidden`) for a tournament outside the caller's tenant, before anything else.
- Errors are RFC 7807 problems: `code` is the stable error code and `detail` its message key (`players.errors.*`, `eligibility_profiles.errors.*`, `registrations.errors.*`).

## Players

| Route | Result |
| --- | --- |
| `GET …/players?search=&isActive=&page=&pageSize=` | `200` page `{ items, page, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }` |
| `GET …/players/{id}` | `200` player, `404 players.not_found` |
| `POST …/players` | `201` player, `400 players.*`, `409 players.email_taken` or `players.document_taken` |
| `PUT …/players/{id}` | `200` player, `400`, `404`, `409` as above |

- `search` is a case-insensitive contains match on first name, last name, email or document. Results are ordered by last name, first name and id. `page` defaults to 1, `pageSize` to 20 (at most 200).
- Body: `{ firstName, lastName, email?, phone?, documentId?, birthDate?, genderId?, categoryId?, club?, isActive }`. `isActive` is only read on update. `genderId` and `categoryId` must exist in the catalogs (`players.unknown_gender`, `players.unknown_category`).
- Players are never deleted, because their registrations keep referencing them. To retire a player, set `isActive: false`; new registrations then refuse that player. The user link (`userId`) is read-only here.

## Eligibility profiles

| Route | Result |
| --- | --- |
| `GET …/eligibility-profiles` | `200` profiles ordered by name |
| `GET …/eligibility-profiles/{id}` | `200` profile, `404 eligibility_profiles.not_found` |
| `POST …/eligibility-profiles` | `201` profile, `400 eligibility_profiles.*`, `409 eligibility_profiles.name_taken` |
| `PUT …/eligibility-profiles/{id}` | `200` profile, `400`, `404`, `409` as above |
| `DELETE …/eligibility-profiles/{id}` | `204`, `404`, `409 eligibility_profiles.in_use` while a tournament references it |

- Body: `{ name, description?, isActive, slots: [{ slotNumber, genderId?, categoryId?, minAge?, maxAge?, label?, required }] }`. `required` defaults to `true`. Slots are returned in slot order.
- This API does not set `tournaments.eligibility_profile_id`. A tournament that references a profile uses the profile's slots for D3.

## Registrations

| Route | Result |
| --- | --- |
| `GET …/registrations?status=` | `200 { items, capacity: { maxPairs, confirmed, pending, waitlisted, placesLeft } }` |
| `GET …/registrations/{registrationId}` | `200` registration, `404 registrations.not_found` |
| `POST …/registrations` | `201` registration (see below) |
| `POST …/registrations/{registrationId}/confirm` · `/reject` · `/waitlist` · `/cancel` | `200` registration at its next revision |
| `PUT …/registrations/{registrationId}/payment` | `200` registration |
| `GET …/registrations/draw-participants` | `200 [DrawParticipant]` |

- Enum values are camelCase. `status` is one of `pending`, `confirmed`, `rejected`, `waitlisted`, `cancelled`. `source` is `public` or `admin`. `paymentStatus` is one of `notRequired`, `pending`, `paid`, `refunded`, `failed`.
- `capacity` always counts every registration of the tournament, even when `status` filters the items.
- **Create.** The body is `{ participants: [{ slotNumber, playerId }], status: pending|confirmed, contactEmail?, notes?, publicNameConsent }`. Every player must be an active player of the tournament's organization (`404 registrations.player_not_found`). The D3 check comes next. The store then decides the final status: a pending registration becomes `waitlisted` when confirmed plus pending already reach `maxPairs`. It refuses `409 registrations.player_already_registered`, and `409 registrations.capacity_reached` for a confirmed request without a place. `paymentStatus` starts as `pending` when the tournament has a fee, else `notRequired`.
- **Status changes (D2).** `pending` can move to `confirmed`, `rejected`, `waitlisted` or `cancelled`. `waitlisted` can move to `confirmed` or `cancelled`. `confirmed` can move to `cancelled`. Any other change is `409 registrations.invalid_transition`. Confirming beyond `maxPairs` is `409 registrations.capacity_reached`.
- **Payment (D5).** The body `{ paymentStatus, revision? }` accepts `paid` (from `pending` or `failed`) or `refunded` (from `paid`), in any registration status. Any other value is `400 registrations.invalid_payment_status`. Any other origin is `409 registrations.invalid_payment_transition`.
- **Revisions.** Status and payment requests accept an optional `revision`. When it is no longer current, the request fails with `409 registrations.concurrent_modification` and writes nothing. A change committed by someone else between the read and the write fails the same way.

### D3 eligibility

The rules come from the tournament's eligibility profile slots. Without a profile, they come from the modality:

| Modality | Slots |
| --- | --- |
| `single` | 1 required slot |
| `doubles`, none or unknown | 2 required slots |
| `teams` | up to 16 optional slots |

Without a profile, each slot carries the tournament's gender and category and has no age limits. The checks run in this order:

1. A filled slot outside the rules returns `400 registrations.invalid_slot`.
2. An empty required slot returns `400 registrations.required_slot_missing`.
3. Then each slot, in slot order:
   - **Gender:** must be equal (`registrations.gender_mismatch`). The `mixed` gender admits any player.
   - **Category:** must be at most one level away (`registrations.category_out_of_range`).
   - **Age:** whole years today (UTC) must fall within `minAge`..`maxAge` (`registrations.age_out_of_range`).

Missing player data (gender, category or birth date) fails any rule that needs it.

### DrawParticipant

The draw participants are the confirmed registrations in creation order, in the shape of the draw request participants (`TournamentDrawRequestBody.participants`):

```json
{ "id": "<registration id>", "displayName": "Ana Gil / Bea Paz", "members": ["Ana Gil", "Bea Paz"], "seedRank": null, "separationKey": "Norte" }
```

`members` are the player names in slot order. `separationKey` is the club that all members share, otherwise the first member's club. Both cases resolve to the first member's club, which is `null` when that member has no club.
