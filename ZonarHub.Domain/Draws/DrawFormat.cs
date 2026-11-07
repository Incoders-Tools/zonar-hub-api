namespace ZonarHub.Domain.Draws;

/// <summary>
/// Competition structure the draw generates.
/// </summary>
public enum DrawFormat
{
    /// <summary>Knockout bracket; non-power-of-two counts receive byes.</summary>
    SingleElimination = 1,

    /// <summary>Everyone plays everyone, in one or two legs.</summary>
    RoundRobin = 2,

    /// <summary>Round-robin groups whose best-placed participants enter a knockout bracket.</summary>
    GroupsThenKnockout = 3,
}
