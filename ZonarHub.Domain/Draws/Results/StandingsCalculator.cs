namespace ZonarHub.Domain.Draws.Results;

/// <summary>
/// Ranks the participants of a group or round robin over its decided matches.
/// </summary>
/// <remarks>
/// Counting: a completed match counts its finished sets and every game; a super tie-break deciding set counts as one
/// set and one game for its winner (its points are not games). A retired match is won by its winner side and counts
/// the sets finished and the games recorded before the retirement (a partial super tie-break adds nothing). A walkover
/// counts as won in straight sets at 6-0 each: 2-0 sets and 12-0 games when best of 3.
/// <para>
/// Order: matches won, then set difference, then game difference, then, when exactly two participants are still tied,
/// their head-to-head wins, then the original order (group distribution order, or the round-robin seed order).
/// </para>
/// </remarks>
internal static class StandingsCalculator
{
    private const int WalkoverGamesPerSet = 6;

    public static Standings Compute(
        string phaseId,
        string? groupId,
        IReadOnlyList<string> participantOrder,
        IReadOnlyList<ResolvedMatch> matches,
        ScoringRules rules)
    {
        var tallies = participantOrder
            .Select((participantId, index) => new Tally(participantId, index))
            .ToDictionary(tally => tally.ParticipantId, StringComparer.Ordinal);
        var headToHead = new Dictionary<(string Winner, string Loser), int>();

        foreach (var match in matches)
        {
            if (match.WinnerParticipantId is not { } winnerId || match.LoserParticipantId is not { } loserId)
                continue;

            var score = Score(match.Result!, rules);
            var winnerIsHome = match.Result!.WinnerSide == MatchSide.Home;
            tallies[winnerId].Add(won: true, winnerIsHome ? score : score.Swapped());
            tallies[loserId].Add(won: false, winnerIsHome ? score.Swapped() : score);
            headToHead[(winnerId, loserId)] = headToHead.GetValueOrDefault((winnerId, loserId)) + 1;
        }

        var ordered = tallies.Values
            .OrderByDescending(tally => tally.Won)
            .ThenByDescending(tally => tally.SetsFor - tally.SetsAgainst)
            .ThenByDescending(tally => tally.GamesFor - tally.GamesAgainst)
            .ThenBy(tally => tally.Index)
            .ToList();
        BreakTwoWayTiesByHeadToHead(ordered, headToHead);

        var rows = ordered
            .Select((tally, index) => new StandingRow(
                tally.ParticipantId,
                index + 1,
                tally.Played,
                tally.Won,
                tally.Played - tally.Won,
                tally.SetsFor,
                tally.SetsAgainst,
                tally.GamesFor,
                tally.GamesAgainst))
            .ToList()
            .AsReadOnly();

        return new Standings(phaseId, groupId, rows, matches.All(match => match.Result is { IsDecided: true }));
    }

    /// <summary>Sets and games of a decided result from the home side's point of view.</summary>
    private static SideScore Score(MatchResult result, ScoringRules rules)
    {
        if (result.Status == MatchStatus.Walkover)
        {
            var sets = rules.SetsToWin;
            var walkover = new SideScore(sets, 0, sets * WalkoverGamesPerSet, 0);
            return result.WinnerSide == MatchSide.Home ? walkover : walkover.Swapped();
        }

        var score = new SideScore(0, 0, 0, 0);
        for (var index = 0; index < result.Sets.Count; index++)
        {
            var set = result.Sets[index];
            var superTiebreak = rules.IsSuperTiebreak(index);
            var outcome = SetScoring.Read(set, superTiebreak);
            var (homeWon, awayWon) = (outcome == SetOutcome.HomeWon ? 1 : 0, outcome == SetOutcome.AwayWon ? 1 : 0);

            score = superTiebreak
                ? score.Plus(homeWon, awayWon, homeWon, awayWon)
                : score.Plus(homeWon, awayWon, set.Home, set.Away);
        }

        return score;
    }

    private static void BreakTwoWayTiesByHeadToHead(List<Tally> ordered, IReadOnlyDictionary<(string, string), int> headToHead)
    {
        for (var start = 0; start < ordered.Count;)
        {
            var end = start;
            while (end + 1 < ordered.Count && ordered[end + 1].Ties(ordered[start]))
                end++;

            if (end == start + 1)
            {
                var (first, second) = (ordered[start], ordered[end]);
                if (headToHead.GetValueOrDefault((second.ParticipantId, first.ParticipantId))
                    > headToHead.GetValueOrDefault((first.ParticipantId, second.ParticipantId)))
                {
                    (ordered[start], ordered[end]) = (second, first);
                }
            }

            start = end + 1;
        }
    }

    private readonly record struct SideScore(int SetsFor, int SetsAgainst, int GamesFor, int GamesAgainst)
    {
        public SideScore Swapped() => new(SetsAgainst, SetsFor, GamesAgainst, GamesFor);

        public SideScore Plus(int setsFor, int setsAgainst, int gamesFor, int gamesAgainst) =>
            new(SetsFor + setsFor, SetsAgainst + setsAgainst, GamesFor + gamesFor, GamesAgainst + gamesAgainst);
    }

    private sealed class Tally(string participantId, int index)
    {
        public string ParticipantId { get; } = participantId;

        public int Index { get; } = index;

        public int Played { get; private set; }

        public int Won { get; private set; }

        public int SetsFor { get; private set; }

        public int SetsAgainst { get; private set; }

        public int GamesFor { get; private set; }

        public int GamesAgainst { get; private set; }

        public void Add(bool won, SideScore score)
        {
            Played++;
            Won += won ? 1 : 0;
            SetsFor += score.SetsFor;
            SetsAgainst += score.SetsAgainst;
            GamesFor += score.GamesFor;
            GamesAgainst += score.GamesAgainst;
        }

        public bool Ties(Tally other) =>
            Won == other.Won
            && SetsFor - SetsAgainst == other.SetsFor - other.SetsAgainst
            && GamesFor - GamesAgainst == other.GamesFor - other.GamesAgainst;
    }
}
