namespace ZonarHub.Domain.Draws;

/// <summary>
/// What a single draw participant represents, aligned to the tournament modality. Informational: it does not change
/// the generated structure and is carried to the resulting <see cref="Draw"/>.
/// </summary>
public enum ParticipationFormat
{
    Individual = 1,
    Pair = 2,
    Team = 3,
}
