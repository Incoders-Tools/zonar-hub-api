namespace ZonarHub.Domain.Draws;

/// <summary>
/// A generated draw: the request metadata, the seed used and the ordered phases.
/// </summary>
public sealed record Draw(
    DrawFormat Format,
    ParticipationFormat ParticipationFormat,
    RandomizationMode RandomizationMode,
    ulong Seed,
    IReadOnlyList<DrawParticipant> Participants,
    IReadOnlyList<Phase> Phases)
{
    /// <summary>
    /// Separations that could not be satisfied (see <see cref="DrawParticipant.SeparationKey"/>), in phase order, then
    /// match or group order. Empty when every separation holds or no keys were given; such conflicts never fail a draw.
    /// </summary>
    public IReadOnlyList<SeparationConflict> SeparationConflicts { get; init; } = [];
}
