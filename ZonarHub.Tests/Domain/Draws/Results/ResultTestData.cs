using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;

namespace ZonarHub.Tests.Domain.Draws.Results;

/// <summary>
/// Builders shared by the score validation and progression tests.
/// </summary>
internal static class ResultTestData
{
    public static SetScore S(int home, int away) => new(home, away);

    public static SetScore S(int home, int away, int tiebreakHome, int tiebreakAway) =>
        new(home, away, new TiebreakScore(tiebreakHome, tiebreakAway));

    public static MatchResult Completed(MatchSide winner, params SetScore[] sets) =>
        new("M", MatchStatus.Completed, sets, winner);

    /// <summary>A completed result for <paramref name="matchId"/>, won by <paramref name="winner"/> with the given winner-first set scores.</summary>
    public static MatchResult Win(string matchId, MatchSide winner, params (int Winner, int Loser)[] sets)
    {
        var scores = (sets.Length == 0 ? new (int Winner, int Loser)[] { (6, 4), (6, 3) } : sets)
            .Select(set => winner == MatchSide.Home ? S(set.Winner, set.Loser) : S(set.Loser, set.Winner))
            .ToArray();
        return new MatchResult(matchId, MatchStatus.Completed, scores, winner);
    }

    /// <summary>A completed result where <paramref name="winnerId"/> beats <paramref name="loserId"/> in their (first) match.</summary>
    public static MatchResult Beat(Draw draw, string winnerId, string loserId, params (int Winner, int Loser)[] sets)
    {
        var match = FindMatch(draw, winnerId, loserId);
        return Win(match.Id, ((ParticipantSlot)match.Home).ParticipantId == winnerId ? MatchSide.Home : MatchSide.Away, sets);
    }

    /// <summary>The first match between two participants, in phase and round order.</summary>
    public static Match FindMatch(Draw draw, string first, string second) =>
        draw.Phases.SelectMany(phase => phase.Matches).First(match =>
            DrawTestData.ParticipantIds(match).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { first, second }.Order(StringComparer.Ordinal)));

    public static MatchSide SideOf(Match match, string participantId) =>
        match.Home is ParticipantSlot home && home.ParticipantId == participantId ? MatchSide.Home : MatchSide.Away;

    public static ResolvedDraw Resolve(Draw draw, IReadOnlyCollection<MatchResult> results, ScoringRules? rules = null)
    {
        var resolved = DrawProgression.Resolve(draw, results, rules ?? ScoringRules.Default);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error.Code : string.Empty);
        return resolved.Value;
    }

    public static ResolvedMatch Match(ResolvedDraw resolved, string matchId) =>
        resolved.FindMatch(matchId) ?? throw new InvalidOperationException($"Unknown match {matchId}.");
}
