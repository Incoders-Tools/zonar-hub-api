# Public tournaments API

Anonymous, read-only showcase of tournaments that organizations chose to make public.
Code: `ZonarHub.ApiService/Endpoints/Public/MapPublicTournamentsEndpoints.cs`, `ZonarHub.Application/Features/PublicTournaments/`, `ZonarHub.Infrastructure/Persistence/Supabase/PublicTournamentReadRepository.cs`.

## Endpoints

| Route | Result |
| --- | --- |
| `GET /api/public/tournaments?limit=N` | `200` with `{ "items": [PublicTournament] }` |
| `GET /api/public/tournaments/{slug}` | `200` with one `PublicTournament`, or `404` problem (`code: public_tournaments.not_found`) |

- No authentication. The group is opened explicitly with `AllowAnonymous`, and it only maps GET routes.
- A tournament is listed when `is_public`, `is_active`, it has a `public_slug`, and its status is not `cancelled`. A tournament with no status is listed. Tournaments from every organization are included.
- The list is ordered by `startDate` ascending, then by name. `limit` is optional: it defaults to 100 and is clamped to 1..100.
- The detail route returns `404` for unknown, malformed, non-public, inactive and cancelled slugs alike. Malformed slugs never reach the database.
- `200` responses send `Cache-Control: public, max-age=60`.

## PublicTournament

```json
{
  "slug": "open-norte-abc123",
  "name": "Open Norte",
  "description": "string | null",
  "rules": "string | null",
  "startDate": "2026-11-20",
  "endDate": "2026-11-22",
  "registrationStartDate": "2026-11-01 | null",
  "registrationEndDate": "2026-11-15 | null",
  "status": { "key": "registration_open", "nameEs": "…", "nameEn": "…", "namePt": "…" },
  "sport": { "key": "padel", "name": "Pádel", "icon": "/uploads/sports-icons/padel.svg", "iconSource": "svg" },
  "category": { "key": "cuarta", "name": "Cuarta categoría", "shortName": "4ta" },
  "gender": { "key": "mixed", "name": "Mixto" },
  "modality": { "key": "pairs", "nameEs": "…", "nameEn": "…", "namePt": "…" },
  "complex": { "name": "Complejo Norte", "address": "string | null" },
  "imageUrl": "string | null",
  "coverImageUrl": "string | null",
  "maxPairs": 32,
  "registrationFeePerPair": 25000.00,
  "prizeMoney": 500000.00,
  "organization": {
    "displayName": "Club Norte",
    "logoUrl": "string | null",
    "description": "string | null",
    "type": "estandar | circuito | academia | operadora | marca",
    "links": {
      "websiteUrl": "…", "instagramUrl": "…", "facebookUrl": "…", "tiktokUrl": "…",
      "youtubeUrl": "…", "whatsappPhone": "+5491122334455", "contactEmail": "…"
    }
  }
}
```

- `status`, `sport`, `category`, `gender`, `modality`, `complex` and `organization` may each be `null`.
- In `status`, the names are `null` when the key is not in the status catalog.
- `organization` is `null` unless the organization is active and turned on its public profile.
- `links` contains only the links that have a value, so it can be `{}`.
- The response never includes internal ids (tournament, organization, tenant, complex, courts, sport or catalog ids), `observations`, `selectedCourtIds`, `sumValue`, `pointsToAward`, `isActive`, or created-by data.
