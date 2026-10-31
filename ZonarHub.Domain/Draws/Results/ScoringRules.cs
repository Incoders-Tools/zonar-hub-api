namespace ZonarHub.Domain.Draws.Results;

/// <summary>
/// How matches of a draw are scored.
/// </summary>
public sealed record ScoringRules
{
    /// <param name="bestOf">Maximum number of sets: a positive odd number (3 by default).</param>
    /// <param name="thirdSetSuperTiebreak">
    /// The deciding set (the third one when best of 3) is a super tie-break to 10, won by 2 points.
    /// </param>
    public ScoringRules(int bestOf = 3, bool thirdSetSuperTiebreak = true)
    {
        if (bestOf < 1 || bestOf % 2 == 0)
            throw new ArgumentOutOfRangeException(nameof(bestOf), bestOf, "Best of must be a positive odd number of sets.");

        BestOf = bestOf;
        ThirdSetSuperTiebreak = thirdSetSuperTiebreak;
    }

    /// <summary>Best of 3 sets with a super tie-break deciding set.</summary>
    public static ScoringRules Default { get; } = new();

    public int BestOf { get; }

    public bool ThirdSetSuperTiebreak { get; }

    /// <summary>Sets a side must win to win the match.</summary>
    public int SetsToWin => BestOf / 2 + 1;

    /// <summary>Whether the set at zero-based <paramref name="index"/> is played as a super tie-break.</summary>
    internal bool IsSuperTiebreak(int index) => ThirdSetSuperTiebreak && index == BestOf - 1;
}
