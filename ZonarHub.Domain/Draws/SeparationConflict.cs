namespace ZonarHub.Domain.Draws;

/// <summary>
/// Where participants sharing a separation key meet despite the separation step.
/// </summary>
public enum SeparationConflictKind
{
    /// <summary>They play each other in the first knockout round; <see cref="SeparationConflict.LocationId"/> is the match id.</summary>
    FirstRoundMatch = 1,

    /// <summary>They share a group; <see cref="SeparationConflict.LocationId"/> is the group id.</summary>
    SameGroup = 2,
}

/// <summary>
/// A separation the draw could not satisfy: participants sharing <see cref="Key"/> that still meet in the first knockout
/// round or share a group because no allowed arrangement keeps them apart.
/// </summary>
/// <param name="Key">The shared separation key, trimmed, as given by the first listed participant.</param>
/// <param name="ParticipantIds">The participants sharing the key at that location: home then away for a match, group order for a group.</param>
/// <param name="Kind">Whether the location is a first-round match or a group.</param>
/// <param name="LocationId">The match id (<c>KO-R1-M{n}</c>) or the group id (<c>A</c>, <c>B</c>, ...).</param>
public sealed record SeparationConflict(
    string Key,
    IReadOnlyList<string> ParticipantIds,
    SeparationConflictKind Kind,
    string LocationId);
