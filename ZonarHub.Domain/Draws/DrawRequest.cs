namespace ZonarHub.Domain.Draws;

/// <summary>
/// Input of <see cref="DrawGenerator.Generate"/>.
/// </summary>
public sealed record DrawRequest(
    DrawFormat Format,
    ParticipationFormat ParticipationFormat,
    IReadOnlyList<DrawParticipant> Participants,
    RandomizationPolicy Randomization,
    DrawOptions? Options = null);

/// <summary>
/// Participant count limits of a draw.
/// </summary>
public static class DrawLimits
{
    public const int MinParticipants = 2;
    public const int MaxParticipants = 256;
}
