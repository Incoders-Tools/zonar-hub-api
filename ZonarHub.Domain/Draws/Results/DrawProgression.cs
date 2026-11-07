using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws.Results;

/// <summary>
/// Applies match results to a stored <see cref="Draw"/> and decides which result changes are allowed. Pure and
/// deterministic: the same draw, results and rules always resolve the same way.
/// </summary>
public static class DrawProgression
{
    /// <summary>
    /// Resolves who plays each match and computes the standings (<see cref="StandingsCalculator"/>).
    /// </summary>
    /// <remarks>
    /// Phases and rounds are read in order. A <see cref="WinnerOfSlot"/> or <see cref="LoserOfSlot"/> resolves once its
    /// source match is decided (completed, walkover or retired) between resolved participants; the winner of a bye
    /// match is its only participant, so byes chain without results. A <see cref="GroupPositionSlot"/> resolves once
    /// every match of its group is decided. Unresolved slots stay <c>null</c>. A stored result whose participants are
    /// pending is kept but advances nobody.
    /// <para>
    /// Fails with <see cref="MatchResultErrors.UnknownMatch"/>, <see cref="MatchResultErrors.DuplicateResult"/> or
    /// <see cref="MatchResultErrors.ByeMatch"/> for results that do not fit the draw, or with the
    /// <see cref="MatchResultValidator"/> error of an invalid score.
    /// </para>
    /// </remarks>
    public static Result<ResolvedDraw> Resolve(Draw draw, IReadOnlyCollection<MatchResult> results, ScoringRules rules)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(rules);

        var drawMatches = draw.Phases.SelectMany(phase => phase.Matches).ToDictionary(match => match.Id, StringComparer.Ordinal);
        var resultsByMatch = new Dictionary<string, MatchResult>(StringComparer.Ordinal);

        foreach (var result in results)
        {
            if (result is null)
                throw new ArgumentException("Results cannot contain null entries.", nameof(results));

            if (!drawMatches.TryGetValue(result.MatchId, out var match))
                return Result.Failure<ResolvedDraw>(MatchResultErrors.UnknownMatch);

            if (!resultsByMatch.TryAdd(result.MatchId, result))
                return Result.Failure<ResolvedDraw>(MatchResultErrors.DuplicateResult);

            if (match.IsBye)
                return Result.Failure<ResolvedDraw>(MatchResultErrors.ByeMatch);

            var validation = MatchResultValidator.Validate(result, rules);
            if (validation.IsFailure)
                return Result.Failure<ResolvedDraw>(validation.Error);
        }

        var resolved = new Dictionary<string, ResolvedMatch>(StringComparer.Ordinal);
        var groupStandings = new Dictionary<string, Standings>(StringComparer.Ordinal);
        var matches = new List<ResolvedMatch>(drawMatches.Count);
        var standings = new List<Standings>();

        foreach (var phase in draw.Phases)
        {
            var groupOfMatch = phase.Groups
                .SelectMany(group => group.Matches.Select(match => (MatchId: match.Id, GroupId: group.Id)))
                .ToDictionary(entry => entry.MatchId, entry => entry.GroupId, StringComparer.Ordinal);

            var phaseMatches = new List<ResolvedMatch>();
            foreach (var match in phase.Matches)
            {
                var resolvedMatch = new ResolvedMatch(
                    phase.Id,
                    groupOfMatch.GetValueOrDefault(match.Id),
                    match,
                    ResolveSlot(match.Home),
                    ResolveSlot(match.Away),
                    resultsByMatch.GetValueOrDefault(match.Id));

                resolved[match.Id] = resolvedMatch;
                phaseMatches.Add(resolvedMatch);
            }

            matches.AddRange(phaseMatches);

            foreach (var group in phase.Groups)
            {
                var groupResult = StandingsCalculator.Compute(
                    phase.Id,
                    group.Id,
                    group.ParticipantIds,
                    phaseMatches.Where(match => match.GroupId == group.Id).ToList(),
                    rules);
                groupStandings[group.Id] = groupResult;
                standings.Add(groupResult);
            }

            if (phase.Kind == PhaseKind.RoundRobin)
                standings.Add(StandingsCalculator.Compute(phase.Id, null, RoundRobinOrder(draw), phaseMatches, rules));
        }

        return Result.Success(new ResolvedDraw(draw, rules, matches.AsReadOnly(), standings.AsReadOnly()));

        string? ResolveSlot(Slot slot) => slot switch
        {
            ParticipantSlot participant => participant.ParticipantId,
            WinnerOfSlot winnerOf => resolved.GetValueOrDefault(winnerOf.MatchId)?.WinnerParticipantId,
            LoserOfSlot loserOf => resolved.GetValueOrDefault(loserOf.MatchId)?.LoserParticipantId,
            GroupPositionSlot position => groupStandings.GetValueOrDefault(position.GroupId) is { IsComplete: true } group
                ? group.Rows.ElementAtOrDefault(position.Rank - 1)?.ParticipantId
                : null,
            _ => null,
        };
    }

    /// <summary>
    /// Whether <paramref name="newResult"/> may replace the result of <paramref name="matchId"/> (<c>null</c> clears
    /// it).
    /// </summary>
    /// <remarks>
    /// Fails with <see cref="MatchResultErrors.UnknownMatch"/>, <see cref="MatchResultErrors.ByeMatch"/>,
    /// <see cref="MatchResultErrors.ParticipantsPending"/> while either side is unresolved, the
    /// <see cref="MatchResultValidator"/> error of an invalid score (using <see cref="ResolvedDraw.Rules"/>), or
    /// <see cref="MatchResultErrors.LockedByNextMatch"/> when changing or clearing the stored result is locked: a
    /// knockout match is locked once a match its winner or loser feeds (directly or through a bye) has a result other
    /// than <see cref="MatchStatus.Scheduled"/>; a group match is locked once a match fed by its group's positions has
    /// such a result. A change keeping the status, sets and winner (court or times only) is never locked.
    /// </remarks>
    public static Result CanRecordResult(ResolvedDraw resolved, string matchId, MatchResult? newResult)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(matchId);

        if (newResult is not null && !string.Equals(newResult.MatchId, matchId, StringComparison.Ordinal))
            throw new ArgumentException("The result belongs to another match.", nameof(newResult));

        var match = resolved.FindMatch(matchId);
        if (match is null)
            return Result.Failure(MatchResultErrors.UnknownMatch);

        if (match.IsBye)
            return Result.Failure(MatchResultErrors.ByeMatch);

        if (match.HomeParticipantId is null || match.AwayParticipantId is null)
            return Result.Failure(MatchResultErrors.ParticipantsPending);

        if (newResult is not null)
        {
            var validation = MatchResultValidator.Validate(newResult, resolved.Rules);
            if (validation.IsFailure)
                return validation;
        }

        if (match.Result is not null && !match.Result.HasSameOutcome(newResult) && IsLocked(resolved, match))
            return Result.Failure(MatchResultErrors.LockedByNextMatch);

        return Result.Success();
    }

    private static bool IsLocked(ResolvedDraw resolved, ResolvedMatch match) =>
        match.GroupId is { } groupId
            ? resolved.Matches.Any(next => HasStarted(next)
                && Slots(next).Any(slot => slot is GroupPositionSlot position && position.GroupId == groupId))
            : FeedsAStartedMatch(resolved, match.Id);

    private static bool FeedsAStartedMatch(ResolvedDraw resolved, string matchId) =>
        resolved.Matches
            .Where(next => Slots(next).Any(slot => slot is WinnerOfSlot winnerOf && winnerOf.MatchId == matchId
                || slot is LoserOfSlot loserOf && loserOf.MatchId == matchId))
            .Any(next => HasStarted(next) || (next.IsBye && FeedsAStartedMatch(resolved, next.Id)));

    private static bool HasStarted(ResolvedMatch match) => match.Result is { Status: not MatchStatus.Scheduled };

    private static IEnumerable<Slot> Slots(ResolvedMatch match) => [match.Match.Home, match.Match.Away];

    /// <summary>
    /// Last tie-break of a round robin: seeded mode puts ranked participants first by rank, then the rest in entry
    /// order; random mode keeps the entry order (seed ranks are ignored, as when generating).
    /// </summary>
    private static IReadOnlyList<string> RoundRobinOrder(Draw draw)
    {
        var participants = draw.RandomizationMode == RandomizationMode.Seeded
            ? draw.Participants.OrderBy(participant => participant.SeedRank is null).ThenBy(participant => participant.SeedRank)
            : draw.Participants.AsEnumerable();

        return participants.Select(participant => participant.Id).ToList();
    }
}
