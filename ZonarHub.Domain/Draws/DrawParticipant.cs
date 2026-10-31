namespace ZonarHub.Domain.Draws;

/// <summary>
/// An entry of the draw: an individual, a pair or a team, identified by a stable caller-supplied id.
/// </summary>
public sealed record DrawParticipant
{
    /// <param name="id">Stable caller id, unique within the request (ordinal comparison).</param>
    /// <param name="displayName">Name shown for the entry.</param>
    /// <param name="members">Member names for pairs and teams; copied, never shared with the caller.</param>
    /// <param name="seedRank">Optional seed rank (1 is the top seed); only lower-is-better order matters.</param>
    /// <param name="separationKey">Optional key (e.g. club) for participants that should be kept apart.</param>
    public DrawParticipant(
        string id,
        string displayName,
        IReadOnlyList<string>? members = null,
        int? seedRank = null,
        string? separationKey = null)
    {
        Id = id;
        DisplayName = displayName;
        Members = Array.AsReadOnly(members?.ToArray() ?? []);
        SeedRank = seedRank;
        SeparationKey = separationKey;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<string> Members { get; }

    public int? SeedRank { get; }

    public string? SeparationKey { get; }
}
