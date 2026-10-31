namespace ZonarHub.Domain.Draws;

public enum PhaseKind
{
    /// <summary>Round-robin groups (phase id <c>G</c>), listed in <see cref="Phase.Groups"/>.</summary>
    GroupStage = 1,
    Knockout = 2,

    /// <summary>A single round robin over every participant (phase id <c>RR</c>).</summary>
    RoundRobin = 3,
}

/// <summary>
/// A stage of the draw with its rounds in playing order.
/// </summary>
/// <remarks>
/// In a group stage, round <c>N</c> is matchday <c>N</c> across every group: it merges round <c>N</c> of each group, in
/// group order, with the groups' resting participants. Matches keep their group ids and per-group numbers, and a group
/// with fewer rounds simply contributes nothing to the later matchdays.
/// </remarks>
public sealed record Phase(string Id, PhaseKind Kind, IReadOnlyList<Round> Rounds)
{
    /// <summary>Groups of a <see cref="PhaseKind.GroupStage"/> phase, in id order; empty for other phases.</summary>
    public IReadOnlyList<Group> Groups { get; init; } = [];

    /// <summary>Every match of the phase, round by round.</summary>
    public IEnumerable<Match> Matches => Rounds.SelectMany(round => round.Matches);
}
