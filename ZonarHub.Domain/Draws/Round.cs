namespace ZonarHub.Domain.Draws;

/// <summary>
/// Matches played in the same round. <see cref="Name"/> is a stable localization-ready key
/// (knockout: <c>final</c>, <c>semifinal</c>, <c>quarterfinal</c>, <c>round_of_16</c>, ...; round robin:
/// <c>matchday</c>, numbered by <see cref="Number"/>).
/// </summary>
/// <remarks>
/// A knockout final round also holds the third-place match, when requested, as its second match.
/// </remarks>
public sealed record Round(int Number, string Name, IReadOnlyList<Match> Matches)
{
    /// <summary>
    /// Participants with no match this round: the round-robin bye of an odd participant count. A rest is never a
    /// <see cref="Match"/>, so match counts only include games that are played. Empty for knockout rounds, whose byes
    /// are bye matches that advance their participant.
    /// </summary>
    public IReadOnlyList<string> RestingParticipantIds { get; init; } = [];
}
