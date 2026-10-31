# Draw engine (fixture generation domain)

API repo `R:\repositories\incoders\zonar-hub-api`, branch `feat/draw-engine` stacked on `feat/public-tournament-showcase` (dev lacks the Pkcs build fix). Pure domain engine only: no persistence, endpoints, LLM, PDF or UI in this feature.
Serves GitHub issue Incoders-Tools/zonar-hub-api#1 (engine half) and phase 2 (S7) of `zonar-hub/odd/tasks/public-tournament-showcase.md`. Push/PR/merge are user decisions; merge after the showcase branch.

## Specs

Quoted from issue #1 (user-authored) and the user's choice (L2).

- **S1 Scope.** "Motor de grillas (recomendado) — Dominio puro en la API: eliminación simple con byes, todos contra todos, zonas + eliminatoria, sorteo con semilla. Base para la fase 2 y el issue #1. Sin IA ni endpoints públicos todavía."
- **S2 Modalities.** "Modalities to support in v1: single elimination (with byes for non-power-of-two counts), round robin (single and double), groups + knockout."
- **S3 Randomization.** "Randomization: fully random, or seeded/conditioned. Seeded generation must be deterministic for a given seed so a draw can be regenerated or reproduced."
- **S4 Zones.** "Zones: correct sizing for any participant count, including indivisible cases."
- **S5 Advancement.** "Advancement: computed deterministically to the final, including the odd-man-out and bye paths."
- **S6 Constraint.** "Constraint: a participant can never appear in two matches in the same round."
- **S7 Participation format.** "participation format: individual / pair / team (aligned to `tournament_modalities`)" and "participant list, with each entry as an individual, a pair, or a group".
- **S8 Tests.** "Engine unit tests cover modality math, bye handling, zone sizing, and advancement."
- **S9 Domain gap.** "Draw engine domain — a new bounded context. At minimum: `Draw`, `Zone`, `Match`, `Round`/`Phase`, `Pairing`, `AdvancementRule`, `SeedingPolicy`, `Bye` handling."

### Design decisions (parent, binding for workers)

- **D1 Placement.** New bounded context `ZonarHub.Domain/Draws/`, pure C# (no I/O, no clock, no `System.Random`), following the domain's `Result`/`Error` conventions (`ZonarHub.Domain/Common`). Tests under `ZonarHub.Tests/Domain/Draws/`.
- **D2 Input.** `DrawRequest`: format (`SingleElimination`, `RoundRobin`, `GroupsThenKnockout`), participation format (`Individual`, `Pair`, `Team`; informational, carried to the draw), participants (`DrawParticipant`: stable caller id, display name, optional members list for pairs/teams, optional seed rank ≥ 1 unique, optional separation key), randomization (`Random` or `Seeded`) with an optional 64-bit seed, and format options (round robin legs 1 or 2; groups: either group count or target group size, qualifiers per group, and whether knockout includes a third-place match). Validation errors are domain `Error`s: fewer than 2 participants, more than 256, duplicate participant ids, duplicate or non-positive seed ranks, impossible group options (e.g. a group smaller than 2 or qualifiers ≥ group size), legs not 1 or 2.
- **D3 Determinism.** An internal PRNG with a fixed, documented algorithm (e.g. SplitMix64 or xoshiro256**) and a Fisher-Yates shuffle, so the same request + seed yields an identical draw on every runtime. When no seed is given the generator requires one from the caller (the domain does not read entropy); the seed used is recorded on the draw.
- **D4 Output.** `Draw` → ordered `Phase`s (group stage, knockout) → `Round`s → `Match`es with stable ids (e.g. `KO-R1-M3`, `G-A-R2-M1`). A match has two `Slot`s; a slot is a participant, a `Bye`, `WinnerOf(matchId)`, `LoserOf(matchId)` (third place), or `GroupPosition(group, rank)`. Knockout matches know the match their winner advances to. A bye match auto-advances its participant (the next round slot is resolvable without results). `Zone`/`Group` holds its participants and its round-robin rounds.
- **D5 Seeding.** Seeded participants are placed in standard bracket positions (1 and 2 at opposite ends, 3-4 in opposite quarters, ...); byes go to the highest seeds first; unseeded participants fill remaining positions by seeded shuffle. Random mode shuffles everyone.
- **D6 Round robin.** Circle method; odd counts add a rest slot (that participant has no match that round, recorded as a bye, never as a match). Two legs mirror home/away.
- **D7 Groups.** Group sizes differ by at most 1; snake distribution by seed (seeded) or by shuffle order (random). Knockout from groups is built from `GroupPosition` slots, pairing winners against runners-up of other groups and avoiding same-group first-round matches whenever arithmetically possible; non-power-of-two qualifier counts get byes for the best-placed qualifiers.
- **D8 Separation (conditioned).** Participants sharing a separation key (e.g. club) are kept out of the same group and out of first-round knockout matches when possible; when impossible the draw still succeeds and reports the unavoidable conflicts.

## Tasks

| ID | Specs | Route | Commit |
| --- | --- | --- | --- |
| T1 Core model, validation, PRNG, single elimination with byes and seeding | S2, S3, S5, S6, S7, S8, S9 | worker | `d2ee8ee` (RED 127 failing, GREEN 669/669, build 0 warnings) |
| T2 Round robin, single and double leg, odd counts | S2, S6, S8 | worker | `86dcd58` (RED 117 failing, GREEN 797/797, build 0 warnings) |
| T3 Groups + knockout: zone sizing, snake distribution, cross pairing, byes | S2, S4, S5, S6, S8 | worker | (RED 363 failing, GREEN 1160/1160, build 0 warnings) |
| T4 Separation keys (conditioned randomization) with conflict report | S3, S8 | worker | |

## Log

- **L1** (user, 2026-10-31, verbatim): "Mientras esperamos via libre para deployar y utilizar github actions, sigamos trabajando en el repo. Revisas si existen issues pendientes en GitHub?"
- **L2** (user decision): "Motor de grillas (recomendado)" among: engine, validate frontend issues #1/#2, both.
- **L3** (evidence): open issues — front #1 (complex courts) and #2 (modality required) fixed in code on `dev` (CC-2 `05bde18`, CC-3a..CC-4b2b), awaiting user browser validation; API #1 asks for the public draw generator; its text verifies no bracket/zone/match concept exists in the domain. Its open decisions (LLM gating, anonymous history, abuse control, participant data policy, renderer) do not block the pure engine.
- **L4** (T1 contract for T2-T4): entry `DrawGenerator.Generate(DrawRequest) -> Result<Draw>`; `DrawOptions(KnockoutOptions?, RoundRobinOptions(Legs=1)?, GroupStageOptions(GroupCount?, GroupSize?, QualifiersPerGroup=2)?)`; `Phase(Id, PhaseKind{GroupStage,Knockout}, Rounds)`; `Round(Number, Name key, Matches)`; `Match(Id, RoundNumber, Number, Home, Away, WinnerAdvancesTo?, LoserAdvancesTo?)`; slots `ParticipantSlot`, `ByeSlot`, `WinnerOfSlot`, `LoserOfSlot`, `GroupPositionSlot(GroupId, Rank)`; `DeterministicRandom(seed)` SplitMix64 with `NextInt`, `Shuffle`; internal `KnockoutBracket.Build(phaseId, entrantsBySeed slots, thirdPlace)` reusable for groups. Seed is required in both modes (`draws.seed_required`); Random ignores seed ranks. Ids/round keys (`KO-R{k}-M{n}`, `final`, `round_of_N`) are now contract. Leftover scratch dir `%TEMP%\gplUcDuq\ga9Lf9xg\agent-draw-engine` (coverage output): deletion blocked pending confirmation.
- **L5** (T2 contract for T3): pure round robin is `PhaseKind.RoundRobin`, phase id `RR`; rounds use key `matchday`; a rest is `Round.RestingParticipantIds` (never a match); reusable `internal static RoundRobinSchedule.Build(string idPrefix, IReadOnlyList<string> participantIds, int legs)` returns rounds with ids `{prefix}-R{n}-M{m}`; shared seed ordering `SeedOrder.Arrange` used by all formats; legs error `draws.invalid_round_robin_legs`.
- **L6** (T3 rules): phases `G` (GroupStage, with `Phase.Groups` of `Group(Id, ParticipantIds, Rounds)`) then `KO`; exactly one of GroupCount/GroupSize, default size 4; groups = min(ceil(n/size), floor(n/2)), larger groups first; snake distribution skipping full groups; ids A..Z then AA..; phase `G` rounds merge matchday N across groups; KO entrants in rank tiers (winners first, byes to winners) with a deterministic reorder that avoids first-round same-group meetings for 2+ groups. New codes `draws.group_options_conflict`, `invalid_group_count`, `group_too_small`, `invalid_group_size`, `invalid_qualifiers`, `knockout_min_qualifiers`. Not done (not requested): keeping same-group qualifiers in opposite halves until the final.
