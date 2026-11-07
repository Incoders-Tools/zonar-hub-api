namespace ZonarHub.Domain.Draws;

/// <summary>
/// Deterministic pseudo-random generator for draws: SplitMix64 (Steele, Lea and Flood; reference implementation by
/// Sebastiano Vigna). The algorithm is part of the draw contract: the same seed yields the same sequence on every
/// runtime, so changing it changes every reproduced draw. Not suitable for cryptographic use.
/// </summary>
public sealed class DeterministicRandom(ulong seed)
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state = seed;

    /// <summary>Next 64-bit value of the SplitMix64 sequence.</summary>
    public ulong NextUInt64()
    {
        unchecked
        {
            _state += GoldenGamma;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Uniform value in <c>[0, maxExclusive)</c>.</summary>
    /// <remarks>
    /// Rejects the lowest <c>2^64 mod maxExclusive</c> values so the modulo carries no bias. A rejection happens with
    /// probability below <c>maxExclusive / 2^64</c>, so in practice every call draws exactly one value.
    /// </remarks>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        var bound = (ulong)maxExclusive;
        var threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value < threshold);

        return (int)(value % bound);
    }

    /// <summary>In-place Fisher-Yates shuffle, walking from the last item down.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
