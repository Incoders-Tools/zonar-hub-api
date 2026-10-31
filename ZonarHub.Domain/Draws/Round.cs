namespace ZonarHub.Domain.Draws;

/// <summary>
/// Matches played in the same round. <see cref="Name"/> is a stable localization-ready key
/// (<c>final</c>, <c>semifinal</c>, <c>quarterfinal</c>, <c>round_of_16</c>, ...).
/// </summary>
/// <remarks>
/// A knockout final round also holds the third-place match, when requested, as its second match.
/// </remarks>
public sealed record Round(int Number, string Name, IReadOnlyList<Match> Matches);
