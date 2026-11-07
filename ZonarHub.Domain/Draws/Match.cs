namespace ZonarHub.Domain.Draws;

/// <summary>
/// A pairing of two <see cref="Slot"/>s with a stable id (knockout: <c>KO-R{round}-M{number}</c>; round robin:
/// <c>RR-R{round}-M{number}</c>; group: <c>G-{group}-R{round}-M{number}</c>).
/// </summary>
/// <param name="WinnerAdvancesTo">Id of the match the winner plays next; <c>null</c> for the final.</param>
/// <param name="LoserAdvancesTo">Id of the match the loser plays next (semifinals into the third-place match).</param>
public sealed record Match(
    string Id,
    int RoundNumber,
    int Number,
    Slot Home,
    Slot Away,
    string? WinnerAdvancesTo = null,
    string? LoserAdvancesTo = null)
{
    /// <summary>One side is a bye: the match is not played and the other side advances.</summary>
    public bool IsBye => Home is ByeSlot || Away is ByeSlot;
}
