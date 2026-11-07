using System.Text.Json.Serialization;

namespace ZonarHub.Application.Features.TournamentDraws;

/// <summary>
/// The D7 draw JSON shared by the admin draw endpoints and the public draw endpoint, built by
/// <see cref="TournamentDrawResponseMapper"/>. Enum values are the camelCase <see cref="DrawWireNames"/>; times are UTC.
/// </summary>
/// <param name="Status"><c>draft</c> or <c>published</c> (a preview is a <c>draft</c>).</param>
/// <param name="Seed">Seed used, as an unsigned 64-bit decimal string.</param>
/// <param name="PublishedAt">Publication time; null for drafts.</param>
/// <param name="SeparationConflicts">Admin only (always a list there); null and omitted from the public draw.</param>
public sealed record TournamentDrawResponse(
    string Status,
    string Format,
    string ParticipationFormat,
    string RandomizationMode,
    string Seed,
    DateTime? PublishedAt,
    DrawOptionsResponse Options,
    IReadOnlyList<DrawParticipantResponse> Participants,
    IReadOnlyList<DrawPhaseResponse> Phases,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DrawSeparationConflictResponse>? SeparationConflicts);

/// <summary>Effective options of a draw: absent sections are null; scoring is always present.</summary>
public sealed record DrawOptionsResponse(
    KnockoutOptionsBody? Knockout,
    RoundRobinOptionsBody? RoundRobin,
    GroupStageOptionsBody? Groups,
    ScoringOptionsResponse Scoring);

/// <summary>Scoring rules the results are validated with.</summary>
public sealed record ScoringOptionsResponse(int BestOf, bool ThirdSetSuperTiebreak);

/// <summary>An entry of the draw. Null seed ranks and separation keys are omitted; the public draw never has keys.</summary>
public sealed record DrawParticipantResponse(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Members,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SeedRank,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SeparationKey);

/// <summary>
/// A phase (<c>G</c> group stage, <c>KO</c> knockout, <c>RR</c> round robin). <see cref="Groups"/> holds the group
/// stage's groups, or a single group <c>RR</c> carrying a round robin's standings; empty for a knockout.
/// </summary>
public sealed record DrawPhaseResponse(
    string Id,
    string Kind,
    IReadOnlyList<DrawGroupResponse> Groups,
    IReadOnlyList<DrawRoundResponse> Rounds);

/// <summary>A group with its current standings (in position order).</summary>
public sealed record DrawGroupResponse(
    string Id,
    IReadOnlyList<string> ParticipantIds,
    IReadOnlyList<DrawStandingResponse> Standings);

/// <summary>Tally of one participant over the decided matches of a group or round robin.</summary>
public sealed record DrawStandingResponse(
    string ParticipantId,
    int Played,
    int Won,
    int Lost,
    int SetsFor,
    int SetsAgainst,
    int GamesFor,
    int GamesAgainst,
    int Position);

/// <summary>A round; <see cref="Name"/> is a localization key (<c>final</c>, <c>semifinal</c>, <c>matchday</c>, ...).</summary>
public sealed record DrawRoundResponse(
    int Number,
    string Name,
    IReadOnlyList<string> RestingParticipantIds,
    IReadOnlyList<DrawMatchResponse> Matches);

/// <summary>A match with its slots and its stored result (null when none was recorded).</summary>
public sealed record DrawMatchResponse(
    string Id,
    int RoundNumber,
    int Number,
    DrawSlotResponse Home,
    DrawSlotResponse Away,
    string? WinnerAdvancesTo,
    string? LoserAdvancesTo,
    DrawMatchResultResponse? Result);

/// <summary>
/// One side of a match. Kind <c>participant</c> carries <see cref="ParticipantId"/>; <c>winnerOf</c>/<c>loserOf</c>
/// carry <see cref="MatchId"/>; <c>groupPosition</c> carries <see cref="GroupId"/> and <see cref="Rank"/>; <c>bye</c>
/// carries nothing. <see cref="ResolvedParticipantId"/> is who plays there once known. Null members are omitted.
/// </summary>
public sealed record DrawSlotResponse(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ParticipantId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MatchId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GroupId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Rank = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResolvedParticipantId = null);

/// <summary>A stored match result. Times are UTC.</summary>
/// <param name="CourtId">
/// Court the result was recorded on (D13); admin only, omitted when none was chosen and always in the public draw.
/// </param>
/// <param name="CourtName">Court shown for the match: the chosen court's name or the free-text name.</param>
public sealed record DrawMatchResultResponse(
    string Status,
    IReadOnlyList<DrawSetScoreResponse> Sets,
    string? WinnerSide,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CourtId,
    string? CourtName,
    DateTime? ScheduledAt,
    DateTime? StartedAt,
    DateTime? FinishedAt);

/// <summary>Games of one set and, for a 7-6 set, its tie-break points (null otherwise).</summary>
public sealed record DrawSetScoreResponse(int Home, int Away, TiebreakScoreBody? Tiebreak);

/// <summary>Participants sharing a separation key that the engine could not keep apart (admin only).</summary>
/// <param name="Kind"><c>firstRoundMatch</c> (location is a match id) or <c>sameGroup</c> (location is a group id).</param>
public sealed record DrawSeparationConflictResponse(
    string Key,
    IReadOnlyList<string> ParticipantIds,
    string Kind,
    string LocationId);

/// <summary>Who the draw JSON is for.</summary>
public enum TournamentDrawAudience
{
    /// <summary>Back office: keeps separation keys and conflicts.</summary>
    Admin = 1,

    /// <summary>Anonymous public grid: separation keys and conflicts are dropped.</summary>
    Public = 2,
}
