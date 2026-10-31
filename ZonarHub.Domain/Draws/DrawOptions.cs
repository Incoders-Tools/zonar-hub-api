namespace ZonarHub.Domain.Draws;

/// <summary>
/// Format-specific options of a <see cref="DrawRequest"/>. Options for formats other than the requested one are ignored.
/// </summary>
public sealed record DrawOptions(
    KnockoutOptions? Knockout = null,
    RoundRobinOptions? RoundRobin = null,
    GroupStageOptions? Groups = null);

/// <summary>
/// Options of a knockout bracket, standalone or after a group stage.
/// </summary>
/// <param name="ThirdPlaceMatch">Adds a match between the semifinal losers.</param>
public sealed record KnockoutOptions(bool ThirdPlaceMatch = false);

/// <summary>
/// Options of a round robin.
/// </summary>
/// <param name="Legs">1 (single) or 2 (double, mirrored home/away).</param>
public sealed record RoundRobinOptions(int Legs = 1);

/// <summary>
/// Options of a group stage: either the number of groups or the target group size.
/// </summary>
public sealed record GroupStageOptions(int? GroupCount = null, int? GroupSize = null, int QualifiersPerGroup = 2);
