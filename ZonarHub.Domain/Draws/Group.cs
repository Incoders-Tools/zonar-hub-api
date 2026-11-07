namespace ZonarHub.Domain.Draws;

/// <summary>
/// A group (zone) of a group stage: its participants and their own round robin.
/// </summary>
/// <param name="Id">Letter id: <c>A</c> to <c>Z</c>, then <c>AA</c>, <c>AB</c>, ... (spreadsheet column order).</param>
/// <param name="ParticipantIds">Participants in distribution order (also their initial round-robin circle order).</param>
/// <param name="Rounds">Round robin of the group; match ids are <c>G-{Id}-R{round}-M{number}</c>.</param>
public sealed record Group(string Id, IReadOnlyList<string> ParticipantIds, IReadOnlyList<Round> Rounds)
{
    /// <summary>Every match of the group, round by round.</summary>
    public IEnumerable<Match> Matches => Rounds.SelectMany(round => round.Matches);
}
