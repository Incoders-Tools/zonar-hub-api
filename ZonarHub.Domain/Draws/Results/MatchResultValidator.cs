using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.Draws.Results;

/// <summary>
/// Validates a <see cref="MatchResult"/> against <see cref="ScoringRules"/> (set shapes: <see cref="SetScoring"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="MatchStatus.Scheduled"/>: no sets and no winner.</item>
/// <item><see cref="MatchStatus.InProgress"/>: no winner; finished sets valid, the last set may be partial, and no
/// side has won the match yet.</item>
/// <item><see cref="MatchStatus.Completed"/>: a winner; only finished sets, ending exactly when one side wins
/// <see cref="ScoringRules.SetsToWin"/> sets, and that side is the winner.</item>
/// <item><see cref="MatchStatus.Walkover"/>: a winner and no sets.</item>
/// <item><see cref="MatchStatus.Retired"/>: a winner and a score as in progress (possibly empty).</item>
/// <item>Times: the finish is not before the start when both are given.</item>
/// </list>
/// Sets beyond <see cref="ScoringRules.BestOf"/>, or after one side already won the match, are
/// <see cref="MatchResultErrors.TooManySets"/>.
/// </remarks>
public static class MatchResultValidator
{
    public static Result Validate(MatchResult result, ScoringRules rules)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(rules);

        if (!Enum.IsDefined(result.Status))
            return Result.Failure(MatchResultErrors.InvalidStatus);

        if (result.WinnerSide is { } side && !Enum.IsDefined(side))
            return Result.Failure(MatchResultErrors.InvalidWinnerSide);

        var status = result.Status switch
        {
            MatchStatus.Scheduled => Scheduled(result),
            MatchStatus.Completed => Completed(result, rules),
            MatchStatus.Walkover => Walkover(result),
            _ => Unfinished(result, rules, winnerRequired: result.Status == MatchStatus.Retired),
        };

        if (status.IsFailure)
            return status;

        return result is { StartedAtUtc: { } started, FinishedAtUtc: { } finished } && finished < started
            ? Result.Failure(MatchResultErrors.InvalidTimes)
            : Result.Success();
    }

    /// <summary>The side that won the match with these set counts, or <c>null</c> when nobody has won it yet.</summary>
    internal static MatchSide? Winner(int homeSets, int awaySets, ScoringRules rules) =>
        homeSets == rules.SetsToWin ? MatchSide.Home
        : awaySets == rules.SetsToWin ? MatchSide.Away
        : null;

    private static Result Scheduled(MatchResult result)
    {
        if (result.WinnerSide is not null)
            return Result.Failure(MatchResultErrors.WinnerNotAllowed);

        return result.Sets.Count > 0 ? Result.Failure(MatchResultErrors.SetsNotAllowedForScheduled) : Result.Success();
    }

    private static Result Walkover(MatchResult result)
    {
        if (result.WinnerSide is null)
            return Result.Failure(MatchResultErrors.WinnerRequired);

        return result.Sets.Count > 0 ? Result.Failure(MatchResultErrors.SetsNotAllowedForWalkover) : Result.Success();
    }

    private static Result Completed(MatchResult result, ScoringRules rules)
    {
        if (result.WinnerSide is null)
            return Result.Failure(MatchResultErrors.WinnerRequired);

        var score = ReadSets(result.Sets, rules, allowPartialLastSet: false);
        if (score.IsFailure)
            return Result.Failure(score.Error);

        var winner = Winner(score.Value.Home, score.Value.Away, rules);
        if (winner is null)
            return Result.Failure(MatchResultErrors.IncompleteScore);

        return winner == result.WinnerSide ? Result.Success() : Result.Failure(MatchResultErrors.WinnerMismatch);
    }

    /// <summary>In progress (no winner) or retired (winner required): a partial score nobody has won yet.</summary>
    private static Result Unfinished(MatchResult result, ScoringRules rules, bool winnerRequired)
    {
        if (winnerRequired && result.WinnerSide is null)
            return Result.Failure(MatchResultErrors.WinnerRequired);

        if (!winnerRequired && result.WinnerSide is not null)
            return Result.Failure(MatchResultErrors.WinnerNotAllowed);

        var score = ReadSets(result.Sets, rules, allowPartialLastSet: true);
        if (score.IsFailure)
            return Result.Failure(score.Error);

        return Winner(score.Value.Home, score.Value.Away, rules) is null
            ? Result.Success()
            : Result.Failure(MatchResultErrors.ScoreDecided);
    }

    private static Result<(int Home, int Away)> ReadSets(
        IReadOnlyList<SetScore> sets,
        ScoringRules rules,
        bool allowPartialLastSet)
    {
        if (sets.Count > rules.BestOf)
            return Result.Failure<(int, int)>(MatchResultErrors.TooManySets);

        var (home, away) = (0, 0);
        for (var index = 0; index < sets.Count; index++)
        {
            if (Winner(home, away, rules) is not null)
                return Result.Failure<(int, int)>(MatchResultErrors.TooManySets);

            var superTiebreak = rules.IsSuperTiebreak(index);
            var outcome = SetScoring.Read(sets[index], superTiebreak);
            var partialAllowed = allowPartialLastSet && index == sets.Count - 1;

            if (outcome == SetOutcome.Invalid || (outcome == SetOutcome.Partial && !partialAllowed))
            {
                return Result.Failure<(int, int)>(
                    superTiebreak ? MatchResultErrors.InvalidSuperTiebreak : MatchResultErrors.InvalidSet);
            }

            if (outcome == SetOutcome.HomeWon)
                home++;
            else if (outcome == SetOutcome.AwayWon)
                away++;
        }

        return Result.Success((home, away));
    }
}
