namespace ZonarHub.Domain.Draws;

public enum PhaseKind
{
    GroupStage = 1,
    Knockout = 2,
}

/// <summary>
/// A stage of the draw with its rounds in playing order.
/// </summary>
public sealed record Phase(string Id, PhaseKind Kind, IReadOnlyList<Round> Rounds)
{
    /// <summary>Every match of the phase, round by round.</summary>
    public IEnumerable<Match> Matches => Rounds.SelectMany(round => round.Matches);
}
