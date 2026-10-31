namespace ZonarHub.Domain.Draws.Results;

/// <summary>
/// Lifecycle of a match result. <see cref="Completed"/>, <see cref="Walkover"/> and <see cref="Retired"/> decide the
/// match; the others do not.
/// </summary>
public enum MatchStatus
{
    /// <summary>Not started; may carry the court and schedule only.</summary>
    Scheduled = 1,

    /// <summary>Being played: a partial score and no winner.</summary>
    InProgress = 2,

    /// <summary>Played to the end: a finished score whose winner is <see cref="MatchResult.WinnerSide"/>.</summary>
    Completed = 3,

    /// <summary>Not played: <see cref="MatchResult.WinnerSide"/> advances, with no sets.</summary>
    Walkover = 4,

    /// <summary>One side retired: <see cref="MatchResult.WinnerSide"/> wins, with the partial score reached.</summary>
    Retired = 5,
}

/// <summary>Side of a <see cref="Match"/>: its <see cref="Match.Home"/> or its <see cref="Match.Away"/> slot.</summary>
public enum MatchSide
{
    Home = 1,
    Away = 2,
}

/// <summary>
/// Games of one set from the home and away sides. A super tie-break deciding set stores its points as the games
/// (e.g. 10-8).
/// </summary>
/// <param name="Tiebreak">Optional tie-break points of a 7-6 set.</param>
public sealed record SetScore(int Home, int Away, TiebreakScore? Tiebreak = null);

/// <summary>Points of a set tie-break from the home and away sides.</summary>
public sealed record TiebreakScore(int Home, int Away);

/// <summary>
/// The result of one draw match, validated by <see cref="MatchResultValidator"/>. Times are UTC.
/// </summary>
public sealed record MatchResult
{
    /// <param name="matchId">Id of the draw match (e.g. <c>KO-R1-M3</c>).</param>
    /// <param name="status">Lifecycle status.</param>
    /// <param name="sets">Sets in playing order; copied, never shared with the caller.</param>
    /// <param name="winnerSide">Winning side; required for decided statuses, absent otherwise.</param>
    /// <param name="courtName">Court shown for the match.</param>
    /// <param name="scheduledAtUtc">Planned start.</param>
    /// <param name="startedAtUtc">Actual start.</param>
    /// <param name="finishedAtUtc">Actual end; not before <paramref name="startedAtUtc"/>.</param>
    public MatchResult(
        string matchId,
        MatchStatus status,
        IReadOnlyList<SetScore>? sets = null,
        MatchSide? winnerSide = null,
        string? courtName = null,
        DateTime? scheduledAtUtc = null,
        DateTime? startedAtUtc = null,
        DateTime? finishedAtUtc = null)
    {
        MatchId = matchId;
        Status = status;
        Sets = Array.AsReadOnly(sets?.ToArray() ?? []);
        WinnerSide = winnerSide;
        CourtName = courtName;
        ScheduledAtUtc = scheduledAtUtc;
        StartedAtUtc = startedAtUtc;
        FinishedAtUtc = finishedAtUtc;
    }

    public string MatchId { get; }

    public MatchStatus Status { get; }

    public IReadOnlyList<SetScore> Sets { get; }

    public MatchSide? WinnerSide { get; }

    public string? CourtName { get; init; }

    public DateTime? ScheduledAtUtc { get; init; }

    public DateTime? StartedAtUtc { get; init; }

    public DateTime? FinishedAtUtc { get; init; }

    /// <summary>The match has a winner: completed, walkover or retired.</summary>
    public bool IsDecided => Status is MatchStatus.Completed or MatchStatus.Walkover or MatchStatus.Retired;

    /// <summary>
    /// Same status, sets and winner as <paramref name="other"/>: only details such as the court or times differ, so
    /// the change cannot affect progression or standings.
    /// </summary>
    internal bool HasSameOutcome(MatchResult? other) =>
        other is not null && Status == other.Status && WinnerSide == other.WinnerSide && Sets.SequenceEqual(other.Sets);
}
