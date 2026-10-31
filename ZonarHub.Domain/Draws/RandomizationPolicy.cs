namespace ZonarHub.Domain.Draws;

/// <summary>
/// How participants are placed in the draw.
/// </summary>
public enum RandomizationMode
{
    /// <summary>Everyone is shuffled; seed ranks are ignored.</summary>
    Random = 1,

    /// <summary>Participants with a seed rank take the standard seeded positions; the rest are shuffled.</summary>
    Seeded = 2,
}

/// <summary>
/// Randomization of a draw. <see cref="Seed"/> feeds the deterministic generator (<see cref="DeterministicRandom"/>)
/// in both modes and is required: the domain never reads entropy, so the caller supplies it and the same request and
/// seed always reproduce the same draw.
/// </summary>
public sealed record RandomizationPolicy(RandomizationMode Mode, ulong? Seed)
{
    /// <summary>Shuffles every participant using <paramref name="seed"/>.</summary>
    public static RandomizationPolicy Random(ulong seed) => new(RandomizationMode.Random, seed);

    /// <summary>Honors seed ranks and shuffles the unseeded participants using <paramref name="seed"/>.</summary>
    public static RandomizationPolicy Seeded(ulong seed) => new(RandomizationMode.Seeded, seed);
}
