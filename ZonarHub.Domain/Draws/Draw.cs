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
    IReadOnlyList<Phase> Phases);
