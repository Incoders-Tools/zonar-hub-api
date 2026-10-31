using ZonarHub.Domain.Draws;

namespace ZonarHub.Tests.Domain.Draws;

/// <summary>
/// Known-answer tests pin the SplitMix64 algorithm: changing it would silently change every stored seeded draw.
/// </summary>
public class DeterministicRandomTests
{
    [Fact]
    public void NextUInt64_MatchesTheSplitMix64ReferenceVectors()
    {
        var random = new DeterministicRandom(1234567);

        ulong[] expected =
        [
            6457827717110365317,
            3203168211198807973,
            9817491932198370423,
            4593380528125082431,
            16408922859458223821,
        ];

        Assert.Equal(expected, Enumerable.Range(0, expected.Length).Select(_ => random.NextUInt64()));
    }

    [Fact]
    public void NextUInt64_SeedZero_MatchesReference()
    {
        var random = new DeterministicRandom(0);

        Assert.Equal(16294208416658607535UL, random.NextUInt64());
        Assert.Equal(7960286522194355700UL, random.NextUInt64());
        Assert.Equal(487617019471545679UL, random.NextUInt64());
    }

    [Fact]
    public void Shuffle_IsFisherYatesOverTheBoundedGenerator_KnownAnswer()
    {
        var items = Enumerable.Range(0, 10).ToList();

        new DeterministicRandom(42).Shuffle(items);

        Assert.Equal([0, 9, 5, 8, 6, 4, 7, 2, 1, 3], items);
    }

    [Fact]
    public void Shuffle_SameSeed_SameOrder_DifferentSeed_DifferentOrder()
    {
        var first = Enumerable.Range(0, 16).ToList();
        var second = Enumerable.Range(0, 16).ToList();
        var third = Enumerable.Range(0, 16).ToList();

        new DeterministicRandom(7).Shuffle(first);
        new DeterministicRandom(7).Shuffle(second);
        new DeterministicRandom(8).Shuffle(third);

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
        Assert.Equal(Enumerable.Range(0, 16), first.Order());
    }

    [Fact]
    public void Shuffle_EmptyOrSingleItem_IsUnchanged()
    {
        var empty = new List<int>();
        var single = new List<int> { 5 };

        new DeterministicRandom(1).Shuffle(empty);
        new DeterministicRandom(1).Shuffle(single);

        Assert.Empty(empty);
        Assert.Equal([5], single);
    }

    [Fact]
    public void Shuffle_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DeterministicRandom(1).Shuffle<int>(null!));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(int.MaxValue)]
    public void NextInt_StaysWithinBounds(int maxExclusive)
    {
        var random = new DeterministicRandom(99);

        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextInt(maxExclusive);
            Assert.InRange(value, 0, maxExclusive - 1);
        }
    }

    [Fact]
    public void NextInt_SmallBound_ReachesEveryValue()
    {
        var random = new DeterministicRandom(5);

        var seen = Enumerable.Range(0, 200).Select(_ => random.NextInt(4)).ToHashSet();

        Assert.Equal([0, 1, 2, 3], seen.Order());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextInt_NonPositiveBound_Throws(int maxExclusive)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(1).NextInt(maxExclusive));
    }
}
