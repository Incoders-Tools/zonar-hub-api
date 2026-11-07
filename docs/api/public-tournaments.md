# Public tournaments API

Anonymous, read-only showcase of tournaments that organizations chose to make public.
Code: `ZonarHub.ApiService/Endpoints/Public/MapPublicTournamentsEndpoints.cs`, `ZonarHub.Application/Features/PublicTournaments/`, `ZonarHub.Infrastructure/Persistence/Supabase/PublicTournamentReadRepository.cs`.
The draw route also uses `ZonarHub.Application/Features/TournamentDraws/TournamentDrawResponseMapper.cs` and `ZonarHub.Infrastructure/Persistence/Supabase/TournamentDrawRepository.cs`.

## Endpoints

| Route | Result |
| --- | --- |
| `GET /api/public/tournaments?limit=N` | `200` with `{ "items": [PublicTournament] }` |
| `GET /api/public/tournaments/{slug}` | `200` with one `PublicTournament`, or `404` problem (`code: public_tournaments.not_found`) |
| `GET /api/public/tournaments/{slug}/draw` | `200` with the tournament's `PublicDraw`, or `404` problem (`code: public_tournaments.not_found` or `public_draws.not_found`) |

- No authentication. The group is opened explicitly with `AllowAnonymous`, and it only maps GET routes.
- A tournament is listed when `is_public`, `is_active`, it has a `public_slug`, and its status is not `cancelled`. A tournament with no status is listed. Tournaments from every organization are included.
- The list is ordered by `startDate` ascending, then by name. `limit` is optional: it defaults to 100 and is clamped to 1..100.
- The detail route returns `404` for unknown, malformed, non-public, inactive and cancelled slugs alike. Malformed slugs never reach the database.
- `200` responses of the list and the detail send `Cache-Control: public, max-age=60`.
- The draw route applies the same tournament rule as the detail route: unknown, malformed, non-public, inactive and cancelled slugs return `404` with `public_tournaments.not_found`. A showcase tournament without a published draw (no draw, or a private draft) returns `404` with `public_draws.not_found` (message key `public_draws.errors.not_found`). Its `200` responses send `Cache-Control: public, max-age=15`, because open pages poll the draw about every 30 seconds.

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

## PublicDraw

The draw is resolved with its results when it is read. Winners and byes move into their next match, and standings are computed for each group and for a round robin. A stored result for a match the draw no longer has is ignored.

```json
{
  "status": "published",
  "format": "singleElimination | roundRobin | groupsThenKnockout",
  "participationFormat": "individual | pair | team",
  "randomizationMode": "random | seeded",
  "seed": "7",
  "publishedAt": "2026-10-31T12:00:00Z",
  "options": {
    "knockout": { "thirdPlaceMatch": false },
    "roundRobin": { "legs": 1 },
    "groups": { "groupCount": 2, "groupSize": null, "qualifiersPerGroup": 2 },
    "scoring": { "bestOf": 3, "thirdSetSuperTiebreak": true }
  },
  "participants": [
    { "id": "p1", "displayName": "Pair 1", "members": ["Ana Pérez", "Bea Gómez"], "seedRank": 1 }
  ],
  "phases": [
    {
      "id": "G | KO | RR",
      "kind": "groupStage | knockout | roundRobin",
      "groups": [
        {
          "id": "A",
          "participantIds": ["p1", "p4"],
          "standings": [
            { "participantId": "p1", "played": 1, "won": 1, "lost": 0, "setsFor": 2, "setsAgainst": 0,
              "gamesFor": 12, "gamesAgainst": 8, "position": 1 }
          ]
        }
      ],
      "rounds": [
        {
          "number": 1,
          "name": "semifinal",
          "restingParticipantIds": [],
          "matches": [
            {
              "id": "KO-R1-M1",
              "roundNumber": 1,
              "number": 1,
              "home": { "kind": "participant", "participantId": "p1", "resolvedParticipantId": "p1" },
              "away": { "kind": "participant", "participantId": "p4", "resolvedParticipantId": "p4" },
              "winnerAdvancesTo": "KO-R2-M1",
              "loserAdvancesTo": null,
              "result": {
                "status": "scheduled | inProgress | completed | walkover | retired",
                "sets": [{ "home": 6, "away": 4, "tiebreak": null }, { "home": 7, "away": 6, "tiebreak": { "home": 7, "away": 5 } }],
                "winnerSide": "home | away | null",
                "courtName": "Central | null",
                "scheduledAt": "2026-11-02T12:00:00Z | null",
                "startedAt": "… | null",
                "finishedAt": "… | null"
              }
            }
          ]
        }
      ]
    }
  ]
}
```

- `groups` lists the groups of a group stage. A round robin has a single group with id `RR` that carries its standings. A knockout has `[]`.
- A slot `kind` is one of `participant` (`participantId`), `bye`, `winnerOf` or `loserOf` (`matchId`), or `groupPosition` (`groupId`, `rank`). `resolvedParticipantId` names who plays in the slot once that is known. Members that are null are omitted.
- `result` is `null` until a result, court or schedule is recorded. Round `name` is a localization key (`final`, `semifinal`, `matchday`, …).
- `options` sections that the draw does not use are `null`. `scoring` is always present. A `seedRank` that is null is omitted.
- The public draw never includes `separationKey`, `separationConflicts`, court ids, the submitted request, or tournament, organization or tenant ids. The admin draw endpoints return the same shape plus those separation fields.
