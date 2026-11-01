using System.Globalization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;

namespace ZonarHub.Application.Features.TournamentDraws;

/// <summary>
/// The single mapping from a resolved draw to the D7 JSON (<see cref="TournamentDrawResponse"/>), shared by the admin
/// endpoints and the public endpoint (<see cref="TournamentDrawAudience"/>).
/// </summary>
public static class TournamentDrawResponseMapper
{
    /// <summary>Phase and group id under which a round robin carries its standings (L7).</summary>
    public const string RoundRobinGroupId = "RR";

    /// <param name="resolved">Draw resolved with its results (<see cref="DrawProgression.Resolve"/>).</param>
    /// <param name="status">Stored status (a preview is a draft).</param>
    /// <param name="publishedAtUtc">Publication time, UTC.</param>
    /// <param name="options">Effective format options; scoring comes from <see cref="ResolvedDraw.Rules"/>.</param>
    /// <param name="audience">Public responses drop separation keys and conflicts (omitted from the JSON).</param>
    public static TournamentDrawResponse ToResponse(
        ResolvedDraw resolved,
        TournamentDrawStatus status,
        DateTime? publishedAtUtc,
        DrawOptions? options,
        TournamentDrawAudience audience)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var draw = resolved.Draw;
        var isAdmin = audience == TournamentDrawAudience.Admin;

        return new TournamentDrawResponse(
            status.ToWireName(),
            draw.Format.ToWireName(),
            draw.ParticipationFormat.ToWireName(),
            draw.RandomizationMode.ToWireName(),
            draw.Seed.ToString(CultureInfo.InvariantCulture),
            publishedAtUtc,
            ToOptions(options, resolved.Rules),
            draw.Participants
                .Select(participant => new DrawParticipantResponse(
                    participant.Id,
                    participant.DisplayName,
                    participant.Members,
                    participant.SeedRank,
                    isAdmin ? participant.SeparationKey : null))
                .ToList(),
            draw.Phases.Select(phase => ToPhase(resolved, phase)).ToList(),
            isAdmin
                ? draw.SeparationConflicts
                    .Select(conflict => new DrawSeparationConflictResponse(
                        conflict.Key, conflict.ParticipantIds, conflict.Kind.ToWireName(), conflict.LocationId))
                    .ToList()
                : null);
    }

    private static DrawOptionsResponse ToOptions(DrawOptions? options, ScoringRules scoring) => new(
        options?.Knockout is { } knockout ? new KnockoutOptionsBody(knockout.ThirdPlaceMatch) : null,
        options?.RoundRobin is { } roundRobin ? new RoundRobinOptionsBody(roundRobin.Legs) : null,
        options?.Groups is { } groups
            ? new GroupStageOptionsBody(groups.GroupCount, groups.GroupSize, groups.QualifiersPerGroup)
            : null,
        new ScoringOptionsResponse(scoring.BestOf, scoring.ThirdSetSuperTiebreak));

    private static DrawPhaseResponse ToPhase(ResolvedDraw resolved, Phase phase)
    {
        // Resolve computes standings for every group and every round-robin phase, so the lookups always succeed.
        IReadOnlyList<DrawGroupResponse> groups = phase.Kind == PhaseKind.RoundRobin
            ? [ToGroup(
                RoundRobinGroupId,
                resolved.Draw.Participants.Select(participant => participant.Id).ToList(),
                resolved.FindStandings(phase.Id)!)]
            : phase.Groups.Select(group => ToGroup(group.Id, group.ParticipantIds, resolved.FindStandings(phase.Id, group.Id)!)).ToList();

        return new DrawPhaseResponse(
            phase.Id,
            phase.Kind.ToWireName(),
            groups,
            phase.Rounds
                .Select(round => new DrawRoundResponse(
                    round.Number,
                    round.Name,
                    round.RestingParticipantIds,
                    round.Matches.Select(match => ToMatch(resolved.FindMatch(match.Id)!)).ToList()))
                .ToList());
    }

    private static DrawGroupResponse ToGroup(string id, IReadOnlyList<string> participantIds, Standings standings) => new(
        id,
        participantIds,
        standings.Rows
            .Select(row => new DrawStandingResponse(
                row.ParticipantId,
                row.Played,
                row.Won,
                row.Lost,
                row.SetsFor,
                row.SetsAgainst,
                row.GamesFor,
                row.GamesAgainst,
                row.Position))
            .ToList());

    private static DrawMatchResponse ToMatch(ResolvedMatch resolved)
    {
        var match = resolved.Match;
        return new DrawMatchResponse(
            match.Id,
            match.RoundNumber,
            match.Number,
            ToSlot(match.Home, resolved.HomeParticipantId),
            ToSlot(match.Away, resolved.AwayParticipantId),
            match.WinnerAdvancesTo,
            match.LoserAdvancesTo,
            resolved.Result is { } result ? ToResult(result) : null);
    }

    private static DrawSlotResponse ToSlot(Slot slot, string? resolvedParticipantId) => slot switch
    {
        ParticipantSlot participant => new DrawSlotResponse(
            DrawWireNames.SlotKinds.Participant, ParticipantId: participant.ParticipantId, ResolvedParticipantId: resolvedParticipantId),
        WinnerOfSlot winnerOf => new DrawSlotResponse(
            DrawWireNames.SlotKinds.WinnerOf, MatchId: winnerOf.MatchId, ResolvedParticipantId: resolvedParticipantId),
        LoserOfSlot loserOf => new DrawSlotResponse(
            DrawWireNames.SlotKinds.LoserOf, MatchId: loserOf.MatchId, ResolvedParticipantId: resolvedParticipantId),
        GroupPositionSlot position => new DrawSlotResponse(
            DrawWireNames.SlotKinds.GroupPosition,
            GroupId: position.GroupId,
            Rank: position.Rank,
            ResolvedParticipantId: resolvedParticipantId),
        _ => new DrawSlotResponse(DrawWireNames.SlotKinds.Bye),
    };

    private static DrawMatchResultResponse ToResult(MatchResult result) => new(
        result.Status.ToWireName(),
        result.Sets
            .Select(set => new DrawSetScoreResponse(
                set.Home,
                set.Away,
                set.Tiebreak is { } tiebreak ? new TiebreakScoreBody(tiebreak.Home, tiebreak.Away) : null))
            .ToList(),
        result.WinnerSide?.ToWireName(),
        result.CourtName,
        result.ScheduledAtUtc,
        result.StartedAtUtc,
        result.FinishedAtUtc);
}
