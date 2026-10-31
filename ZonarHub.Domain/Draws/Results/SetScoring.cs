namespace ZonarHub.Domain.Draws.Results;

internal enum SetOutcome
{
    Invalid = 0,

    /// <summary>A valid score of a set still being played.</summary>
    Partial = 1,
    HomeWon = 2,
    AwayWon = 3,
}

/// <summary>
/// Reads one set score, shared by the result validation and the standings.
/// </summary>
/// <remarks>
/// A regular set is won 6-0 to 6-4, 7-5 or 7-6; its optional tie-break points belong to a 7-6 set only and are won by
/// the set winner, reaching 7 with a 2-point lead (beyond 7, by exactly 2). A partial regular set has at most 6 games
/// a side (6-5 and 6-6 included) and no tie-break points. A super tie-break is won reaching 10 with a 2-point lead
/// (beyond 10, by exactly 2: 10-8, 11-9, 12-10); it is partial below 10 points, or beyond with at most one point of
/// difference (10-9, 11-11), and never carries tie-break points.
/// </remarks>
internal static class SetScoring
{
    private const int SetGames = 6;
    private const int TiebreakPoints = 7;
    private const int SuperTiebreakPoints = 10;

    public static SetOutcome Read(SetScore? set, bool superTiebreak)
    {
        if (set is null || set.Home < 0 || set.Away < 0)
            return SetOutcome.Invalid;

        return superTiebreak ? SuperTiebreak(set) : Regular(set);
    }

    private static SetOutcome Regular(SetScore set)
    {
        var (winner, loser) = (Math.Max(set.Home, set.Away), Math.Min(set.Home, set.Away));
        var finished = (winner == SetGames && loser <= SetGames - 2) || (winner == SetGames + 1 && loser is SetGames - 1 or SetGames);

        if (!finished)
            return set.Tiebreak is null && winner <= SetGames ? SetOutcome.Partial : SetOutcome.Invalid;

        var won = set.Home > set.Away ? SetOutcome.HomeWon : SetOutcome.AwayWon;
        if (set.Tiebreak is not { } tiebreak)
            return won;

        var tiebreakValid = loser == SetGames
            && tiebreak.Home >= 0
            && tiebreak.Away >= 0
            && (tiebreak.Home > tiebreak.Away) == (set.Home > set.Away)
            && IsWon(Math.Max(tiebreak.Home, tiebreak.Away), Math.Min(tiebreak.Home, tiebreak.Away), TiebreakPoints);

        return tiebreakValid ? won : SetOutcome.Invalid;
    }

    private static SetOutcome SuperTiebreak(SetScore set)
    {
        if (set.Tiebreak is not null)
            return SetOutcome.Invalid;

        var (winner, loser) = (Math.Max(set.Home, set.Away), Math.Min(set.Home, set.Away));
        if (IsWon(winner, loser, SuperTiebreakPoints))
            return set.Home > set.Away ? SetOutcome.HomeWon : SetOutcome.AwayWon;

        return winner < SuperTiebreakPoints || winner - loser <= 1 ? SetOutcome.Partial : SetOutcome.Invalid;
    }

    /// <summary>Reaching <paramref name="target"/> with a 2-point lead, or beyond it with exactly 2.</summary>
    private static bool IsWon(int winner, int loser, int target) =>
        (winner == target && loser <= target - 2) || (winner > target && winner - loser == 2);
}
