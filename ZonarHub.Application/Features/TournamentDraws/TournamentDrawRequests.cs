namespace ZonarHub.Application.Features.TournamentDraws;

/// <summary>
/// Body of <c>POST /api/admin/tournaments/{tournamentId}/draw/preview</c> and <c>PUT /api/admin/tournaments/{tournamentId}/draw</c>.
/// Enum values use the camelCase wire names of <see cref="DrawWireNames"/>.
/// </summary>
/// <param name="Format"><c>singleElimination</c>, <c>roundRobin</c> or <c>groupsThenKnockout</c>.</param>
/// <param name="ParticipationFormat"><c>individual</c>, <c>pair</c> or <c>team</c>.</param>
/// <param name="Randomization">Placement mode and optional seed.</param>
/// <param name="Options">Format options and scoring; absent sections take the engine defaults.</param>
/// <param name="Participants">Between 2 and 256 entries with unique ids.</param>
public sealed record TournamentDrawRequestBody(
    string? Format,
    string? ParticipationFormat,
    DrawRandomizationBody? Randomization,
    DrawOptionsBody? Options,
    IReadOnlyList<DrawParticipantBody?>? Participants);

/// <summary>Randomization of a draw request.</summary>
/// <param name="Mode"><c>random</c> (seed ranks ignored) or <c>seeded</c>.</param>
/// <param name="Seed">
/// Unsigned 64-bit decimal string. Absent or blank: the API generates one and returns it in the draw, so the same
/// request with that seed reproduces the draw.
/// </param>
public sealed record DrawRandomizationBody(string? Mode, string? Seed = null);

/// <summary>Options of a draw request (and of a returned draw).</summary>
public sealed record DrawOptionsBody(
    KnockoutOptionsBody? Knockout = null,
    RoundRobinOptionsBody? RoundRobin = null,
    GroupStageOptionsBody? Groups = null,
    ScoringOptionsBody? Scoring = null);

/// <summary>Knockout options.</summary>
/// <param name="ThirdPlaceMatch">Adds a match between the semifinal losers.</param>
public sealed record KnockoutOptionsBody(bool ThirdPlaceMatch = false);

/// <summary>Round robin options.</summary>
/// <param name="Legs">1 (single) or 2 (double).</param>
public sealed record RoundRobinOptionsBody(int Legs = 1);

/// <summary>Group stage options: either the group count or the group size, never both.</summary>
public sealed record GroupStageOptionsBody(int? GroupCount = null, int? GroupSize = null, int QualifiersPerGroup = 2);

/// <summary>Scoring of the draw's matches (best of 3 sets).</summary>
/// <param name="ThirdSetSuperTiebreak">The deciding set is a super tie-break to 10 (default true).</param>
public sealed record ScoringOptionsBody(bool ThirdSetSuperTiebreak = true);

/// <summary>An entry of a draw request.</summary>
/// <param name="Id">Draw-local id, unique within the request.</param>
/// <param name="DisplayName">Name shown for the entry.</param>
/// <param name="Members">Member names of a pair or team.</param>
/// <param name="SeedRank">Optional seed rank (1 is the top seed).</param>
/// <param name="SeparationKey">Optional club or organization kept apart in groups and the first knockout round.</param>
public sealed record DrawParticipantBody(
    string? Id,
    string? DisplayName,
    IReadOnlyList<string>? Members = null,
    int? SeedRank = null,
    string? SeparationKey = null);

/// <summary>
/// Body of <c>PUT /api/admin/tournaments/{tournamentId}/draw/matches/{matchId}/result</c>. Status <c>scheduled</c> with
/// no sets and no winner clears the score while keeping the court and schedule.
/// </summary>
/// <param name="Status"><c>scheduled</c>, <c>inProgress</c>, <c>completed</c>, <c>walkover</c> or <c>retired</c>.</param>
/// <param name="Sets">Sets in playing order; a 7-6 set may carry its tie-break points.</param>
/// <param name="WinnerSide"><c>home</c> or <c>away</c>; required for completed, walkover and retired.</param>
/// <param name="CourtId">Optional court of the organization.</param>
/// <param name="CourtName">Court shown for the match.</param>
/// <param name="ScheduledAt">Planned start (ISO-8601 with offset; stored in UTC).</param>
/// <param name="StartedAt">Actual start (ISO-8601 with offset; stored in UTC).</param>
/// <param name="FinishedAt">Actual end (ISO-8601 with offset; stored in UTC).</param>
public sealed record MatchResultBody(
    string? Status,
    IReadOnlyList<SetScoreBody?>? Sets,
    string? WinnerSide,
    Guid? CourtId = null,
    string? CourtName = null,
    DateTimeOffset? ScheduledAt = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null);

/// <summary>Games of one set (a super tie-break deciding set stores its points as the games, e.g. 10-8).</summary>
public sealed record SetScoreBody(int Home, int Away, TiebreakScoreBody? Tiebreak = null);

/// <summary>Tie-break points of a 7-6 set.</summary>
public sealed record TiebreakScoreBody(int Home, int Away);
