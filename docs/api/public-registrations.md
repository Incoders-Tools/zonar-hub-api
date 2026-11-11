# Public registrations API

Anonymous registration of a pair on the public tournament page, verified by an emailed code (S2, S4; D2, D3, D4 of `zonar-hub/odd/tasks/registrations.md`).
Code: `ZonarHub.ApiService/Endpoints/Public/MapPublicRegistrationsEndpoints.cs`, `ZonarHub.Application/Features/PublicRegistrations/`. The slot rules are shared with the backoffice (`ZonarHub.Application/Features/TournamentRegistrations/TournamentEligibilitySlots.cs`, see [admin-registrations.md](admin-registrations.md)).

## Endpoints

| Route | Result | Rate limit |
| --- | --- | --- |
| `GET /api/public/tournaments/{slug}/registration` | `200` form data | global |
| `POST /api/public/tournaments/{slug}/registration/code` | `202` `{ expiresInSeconds, resendAfterSeconds }` | `public-registration-code` |
| `POST /api/public/tournaments/{slug}/registration` | `201` `{ reference, status, payment }` | `anonymous-auth` |
| `GET /api/public/tournaments/{slug}/confirmed-pairs` | `200` `{ items }` | global |

- No authentication. The group is opened explicitly with `AllowAnonymous`.
- Every route uses the visibility rule of the public tournament page. Unknown, malformed, non-public, inactive and cancelled slugs return `404 public_tournaments.not_found`.
- Errors are RFC 7807 problems: `code` is the stable error code and `detail` its message key (`public_registrations.errors.*`, `players.errors.*`, `registrations.errors.*`).
- No response carries player or organization ids, emails, phones, documents or birth dates.

## Form data

`GET …/registration` returns:

```json
{
  "slug": "open-norte-abc123",
  "state": "open",
  "isOpen": true,
  "opensOn": "2026-11-01",
  "closesOn": "2026-11-15",
  "capacity": { "maxPairs": 16, "placesLeft": 3, "waitlistOnly": false },
  "fee": { "amountPerPair": 20000, "required": true },
  "slots": [{ "slotNumber": 1, "label": null, "required": true, "genderId": "…", "categoryId": "…", "minAge": null, "maxAge": null }],
  "categoryTolerance": 1,
  "consentTextKey": "public_registrations.consent.public_name"
}
```

- **Window (D2).** `state` is `open`, `notStarted` or `ended`. The window runs from `registrationStartDate` to `registrationEndDate`, both days included, with today taken in UTC. A missing date does not restrict its side.
- **Capacity.** `placesLeft` counts confirmed and pending registrations. `waitlistOnly` is true when a new registration would be waitlisted. Both `maxPairs` and `placesLeft` are null when the tournament has no limit.
- **Slots (D3).** The slots come from the tournament's active eligibility profile. Without a profile, or when it is inactive (L8), they come from the modality with the tournament's gender and category. `genderId` and `categoryId` are ids of the anonymous catalogs `/api/catalog/genders` and `/api/catalog/categories`. Null means no rule, and the `mixed` gender admits any player. A referenced profile that is not the organization's returns `404 eligibility_profiles.not_found`.
- `200` responses send `Cache-Control: public, max-age=15`.

## Email code

`POST …/registration/code` with `{ contactEmail }` sends a 6-digit code to that address.

- The code is valid for 15 minutes, and only for this tournament and address. It is not the sign-up verification code and does not use its cache key.
- Only a salted SHA-256 hash is cached, under `public-registration-code:{tournamentId}:{email}`.
- A new code replaces the previous one. A new request for the same address and tournament within 60 seconds returns `429 public_registrations.code_cooldown` and sends nothing.
- `400 registrations.contact_email_required` or `registrations.invalid_contact_email`. `409 public_registrations.closed` outside the window.
- The email uses the template `public_registrations.verification_code` (variables `code`, `expires_minutes`, `tournament_name`). Without a stored template it falls back to a built-in Spanish message.
- The `public-registration-code` policy allows `Security:PublicRegistrationCodePermitLimit` requests (default 10) per caller every `Security:WindowSeconds` (default 60). Beyond that the response is `429` without a body.

## Submit

`POST …/registration` with:

```json
{
  "contactEmail": "contact@club.test",
  "code": "123456",
  "participants": [
    { "slotNumber": 1, "firstName": "Ana", "lastName": "Gil", "email": "ana@club.test", "phone": null, "documentId": "30111222", "birthDate": "1990-05-01", "genderId": "…", "categoryId": "…", "club": "Norte" }
  ],
  "publicNameConsent": true
}
```

It returns `201` with `{ "reference": "…", "status": "pending", "payment": { "status": "pending", "amount": 20000 } }`.

- **Players.** Each participant becomes the organization's player with that email (any case), else with that document, else a new player. An existing player is never overwritten. Only its empty fields (email, phone, document, birth date, gender, category, club) are filled. Its stored data is what the eligibility rules read.
- **Status (S4, D2).** The registration is `pending`. It is `waitlisted` when confirmed plus pending registrations already reach `maxPairs`. The organizer confirms or rejects it in the backoffice.
- **Payment.** `payment.status` is `pending` with the fee as `amount` when the fee is positive, else `notRequired` with a null `amount`. Online payment fields will be added to `payment` without changing these.
- **Reference.** The registration's id, to quote to the organizer.
- **Order of checks.** Nothing is written on an error, and the code stays usable:
  1. `409 public_registrations.closed` outside the window. `404 eligibility_profiles.not_found` when the referenced profile is not the organization's.
  2. Input: `400 registrations.contact_email_required`, `invalid_contact_email`, `participants_required`, `players.*` (names, email, lengths, `birth_date_in_future`, `unknown_gender`, `unknown_category`), and `registrations.duplicate_player` when an email or document repeats.
  3. `400 public_registrations.invalid_code`: missing, wrong, expired, already used, or the code of another address or tournament. Five wrong attempts remove the code.
  4. `409 public_registrations.participant_conflict` when the email and the document belong to two different players. `409 public_registrations.participant_unavailable` when the player was deactivated.
  5. `400 registrations.invalid_slot`, `required_slot_missing`, `duplicate_player`. `409 registrations.player_already_registered` when a player is in another active registration of the tournament.
  6. D3 eligibility: `400 registrations.gender_mismatch`, `category_out_of_range`, `age_out_of_range`. Missing player data fails the rule that needs it (L8).
- After the checks, the code is consumed, then the players and the registration are written.

## Confirmed pairs

`GET …/confirmed-pairs` returns the confirmed registrations in registration order:

```json
{ "items": [
  { "position": 1, "displayName": "Ana Gil / Bea Paz", "members": ["Ana Gil", "Bea Paz"], "labelKey": null },
  { "position": 2, "displayName": null, "members": [], "labelKey": "public_registrations.confirmed_pairs.anonymous" }
] }
```

- Names appear only for registrations with `publicNameConsent`. The others carry only the translatable `labelKey`.
- `200` responses send `Cache-Control: public, max-age=60`.
