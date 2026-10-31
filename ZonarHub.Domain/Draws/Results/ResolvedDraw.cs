namespace ZonarHub.Domain.Draws.Results;

/// <summary>
/// A <see cref="Draw"/> with its results applied by <see cref="DrawProgression.Resolve"/>: who plays each match and
/// the standings of each group and round-robin phase.
/// </summary>
public sealed class ResolvedDraw
{
    private readonly IReadOnlyDictionary<string, ResolvedMatch> _matchesById;

    internal ResolvedDraw(
        Draw draw,
        ScoringRules rules,
        IReadOnlyList<ResolvedMatch> matches,
        IReadOnlyList<Standings> standings)
    {
        Draw = draw;
        Rules = rules;
        Matches = matches;
        Standings = standings;
        _matchesById = matches.ToDictionary(match => match.Id, StringComparer.Ordinal);
    }

    public Draw Draw { get; }

    /// <summary>Rules the results were read with; also used by <see cref="DrawProgression.CanRecordResult"/>.</summary>
    public ScoringRules Rules { get; }

    /// <summary>Every match of the draw, in phase then round order.</summary>
    public IReadOnlyList<ResolvedMatch> Matches { get; }

    /// <summary>Standings of each group (in group order) and of each round-robin phase; none for a knockout.</summary>
    public IReadOnlyList<Standings> Standings { get; }

    /// <summary>The match with <paramref name="matchId"/>, or <c>null</c> when the draw has none.</summary>
    public ResolvedMatch? FindMatch(string matchId) => _matchesById.GetValueOrDefault(matchId);

    /// <summary>
    /// Standings of group <paramref name="groupId"/> of phase <paramref name="phaseId"/>, or of the whole phase when
    /// <paramref name="groupId"/> is <c>null</c> (round robin); <c>null</c> when there are none.
    /// </summary>
    public Standings? FindStandings(string phaseId, string? groupId = null) =>
        Standings.FirstOrDefault(standings => standings.PhaseId == phaseId && standings.GroupId == groupId);
}

/// <summary>
/// A match with the participants resolved from its slots (<c>null</c> while unknown) and its result, if any.
/// </summary>
/// <param name="GroupId">Group of a group-stage match; <c>null</c> otherwise.</param>
/// <param name="Result">Stored result; <c>null</c> when none was recorded (always for a bye).</param>
public sealed record ResolvedMatch(
    string PhaseId,
    string? GroupId,
    Match Match,
    string? HomeParticipantId,
    string? AwayParticipantId,
    MatchResult? Result)
{
    public string Id => Match.Id;

    public bool IsBye => Match.IsBye;

    /// <summary>
    /// The participant advancing: the only participant of a bye, or the winning side of a decided result between
    /// resolved participants; otherwise <c>null</c>.
    /// </summary>
    public string? WinnerParticipantId => IsBye ? HomeParticipantId ?? AwayParticipantId : DecidedSide(winner: true);

    /// <summary>The losing side of a decided result between resolved participants; <c>null</c> for a bye.</summary>
    public string? LoserParticipantId => IsBye ? null : DecidedSide(winner: false);

    private string? DecidedSide(bool winner)
    {
        if (Result is not { IsDecided: true, WinnerSide: { } side } || HomeParticipantId is null || AwayParticipantId is null)
            return null;

        return (side == MatchSide.Home) == winner ? HomeParticipantId : AwayParticipantId;
    }
}

/// <summary>
/// Ranking of a group, or of a round-robin phase (<see cref="GroupId"/> <c>null</c>). Rows are in position order;
/// positions are final only when <see cref="IsComplete"/>.
/// </summary>
/// <param name="IsComplete">Every match is decided (completed, walkover or retired).</param>
public sealed record Standings(string PhaseId, string? GroupId, IReadOnlyList<StandingRow> Rows, bool IsComplete);

/// <summary>Tally of one participant over the decided matches of a group or round robin.</summary>
/// <param name="Position">1-based rank in the current order.</param>
public sealed record StandingRow(
    string ParticipantId,
    int Position,
    int Played,
    int Won,
    int Lost,
    int SetsFor,
    int SetsAgainst,
    int GamesFor,
    int GamesAgainst)
{
    public int SetDifference => SetsFor - SetsAgainst;

    public int GameDifference => GamesFor - GamesAgainst;
}
