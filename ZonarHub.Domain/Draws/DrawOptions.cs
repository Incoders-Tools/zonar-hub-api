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
/// Options of a round robin, standalone or inside each group of a group stage.
/// </summary>
/// <param name="Legs">1 (single) or 2 (double, mirrored home/away).</param>
public sealed record RoundRobinOptions(int Legs = 1);

/// <summary>
/// Options of a group stage: either the number of groups or the target group size, never both. Without either, the
/// target group size is 4. Group sizes differ by at most one, larger groups first.
/// </summary>
/// <param name="GroupCount">Number of groups (at least 1); every group must hold at least two participants.</param>
/// <param name="GroupSize">
/// Target group size (at least 2): <c>ceil(participants / size)</c> groups, reduced when needed so no group holds fewer
/// than two participants.
/// </param>
/// <param name="QualifiersPerGroup">
/// Best-placed participants of each group entering the knockout: at least 1 and below the smallest group size.
/// </param>
public sealed record GroupStageOptions(int? GroupCount = null, int? GroupSize = null, int QualifiersPerGroup = 2);
