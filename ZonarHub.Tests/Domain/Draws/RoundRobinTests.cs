using ZonarHub.Domain.Draws;
using static ZonarHub.Tests.Domain.Draws.DrawTestData;

namespace ZonarHub.Tests.Domain.Draws;

public class RoundRobinTests
{
    private static readonly int[] CountValues = [2, 3, 4, 5, 6, 7, 8, 16];

    public static TheoryData<int, int> CountsAndLegs
    {
        get
        {
            var data = new TheoryData<int, int>();
            foreach (var count in CountValues)
            {
                data.Add(count, 1);
                data.Add(count, 2);
            }

            return data;
        }
    }

    public static TheoryData<int> Counts => new(CountValues);

    public static TheoryData<RandomizationMode> Modes => new() { RandomizationMode.Random, RandomizationMode.Seeded };

    private static Phase Schedule(int count, int legs = 1, RandomizationMode mode = RandomizationMode.Seeded, ulong seed = 2026) =>
        RoundRobinPhase(Generate(RoundRobin(Participants(count), new RandomizationPolicy(mode, seed), legs)));

    private static int RoundsPerLeg(int count) => count % 2 == 0 ? count - 1 : count;

    private static IEnumerable<Round> Leg(Phase phase, int count, int leg) =>
        phase.Rounds.Skip((leg - 1) * RoundsPerLeg(count)).Take(RoundsPerLeg(count));

    private static string Home(Match match) => ((ParticipantSlot)match.Home).ParticipantId;

    private static string Away(Match match) => ((ParticipantSlot)match.Away).ParticipantId;

    private static string PairKey(string a, string b) => string.Join('|', new[] { a, b }.Order(StringComparer.Ordinal));

    private static IReadOnlyList<string> Ids(int count) => Enumerable.Range(1, count).Select(i => $"p{i}").ToList();

    [Theory]
    [MemberData(nameof(CountsAndLegs))]
    public void Generate_RoundsMatchesAndIds_FollowTheCircleMethod(int count, int legs)
    {
        var draw = Generate(RoundRobin(Participants(count), legs: legs));
        var phase = RoundRobinPhase(draw);

        Assert.Equal(DrawFormat.RoundRobin, draw.Format);
        Assert.Single(draw.Phases);
        Assert.Equal("RR", phase.Id);
        Assert.Equal(RoundsPerLeg(count) * legs, phase.Rounds.Count);
        Assert.Equal(count * (count - 1) / 2 * legs, phase.Matches.Count());
        for (var r = 0; r < phase.Rounds.Count; r++)
        {
            var round = phase.Rounds[r];
            Assert.Equal(r + 1, round.Number);
            Assert.Equal("matchday", round.Name);
            Assert.Equal(count / 2, round.Matches.Count);
            for (var m = 0; m < round.Matches.Count; m++)
            {
                var match = round.Matches[m];
                Assert.Equal($"RR-R{r + 1}-M{m + 1}", match.Id);
                Assert.Equal(r + 1, match.RoundNumber);
                Assert.Equal(m + 1, match.Number);
            }
        }
    }

    [Theory]
    [MemberData(nameof(CountsAndLegs))]
    public void Generate_MatchesAreBetweenKnownParticipants_WithoutByesOrAdvancement(int count, int legs)
    {
        Assert.All(Schedule(count, legs).Matches, match =>
        {
            Assert.IsType<ParticipantSlot>(match.Home);
            Assert.IsType<ParticipantSlot>(match.Away);
            Assert.NotEqual(match.Home, match.Away);
            Assert.False(match.IsBye);
            Assert.Null(match.WinnerAdvancesTo);
            Assert.Null(match.LoserAdvancesTo);
        });
    }

    [Theory]
    [MemberData(nameof(CountsAndLegs))]
    public void Generate_EveryPairMeetsExactlyOncePerLeg(int count, int legs)
    {
        var phase = Schedule(count, legs);
        var ids = Ids(count);
        var expectedPairs = ids.SelectMany((a, i) => ids.Skip(i + 1).Select(b => PairKey(a, b))).Order().ToList();

        for (var leg = 1; leg <= legs; leg++)
        {
            var pairs = Leg(phase, count, leg)
                .SelectMany(round => round.Matches)
                .Select(match => PairKey(Home(match), Away(match)))
                .Order()
                .ToList();

            Assert.Equal(expectedPairs, pairs);
        }
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void Generate_SecondLeg_MirrorsTheFirstWithHomeAndAwaySwapped(int count)
    {
        var phase = Schedule(count, legs: 2);
        var first = Leg(phase, count, 1).ToList();
        var second = Leg(phase, count, 2).ToList();

        Assert.Equal(first.Count, second.Count);
        for (var r = 0; r < first.Count; r++)
        {
            Assert.Equal(first[r].RestingParticipantIds, second[r].RestingParticipantIds);
            Assert.Equal(
                first[r].Matches.Select(match => (match.Away, match.Home)),
                second[r].Matches.Select(match => (match.Home, match.Away)));
        }
    }

    [Theory]
    [MemberData(nameof(CountsAndLegs))]
    public void Generate_EveryRoundHoldsEachParticipantExactlyOnce_PlayingOrResting(int count, int legs)
    {
        foreach (var round in Schedule(count, legs).Rounds)
        {
            var playing = round.Matches.SelectMany(ParticipantIds).ToList();

            Assert.Equal(playing.Count, playing.Distinct().Count());
            Assert.Empty(playing.Intersect(round.RestingParticipantIds));
            Assert.Equal(Ids(count).Order(), playing.Concat(round.RestingParticipantIds).Order());
        }
    }

    [Theory]
    [MemberData(nameof(CountsAndLegs))]
    public void Generate_OddCountRestsOneParticipantPerRound_AndEachRestsOncePerLeg_EvenCountNeverRests(int count, int legs)
    {
        var phase = Schedule(count, legs);

        for (var leg = 1; leg <= legs; leg++)
        {
            var rounds = Leg(phase, count, leg).ToList();
            if (count % 2 == 0)
            {
                Assert.All(rounds, round => Assert.Empty(round.RestingParticipantIds));
            }
            else
            {
                Assert.All(rounds, round => Assert.Single(round.RestingParticipantIds));
                Assert.Equal(Ids(count).Order(), rounds.SelectMany(round => round.RestingParticipantIds).Order());
            }
        }
    }

    [Theory]
    [MemberData(nameof(CountsAndLegs))]
    public void Generate_HomeAndAwayCountsDifferByAtMostOnePerLeg(int count, int legs)
    {
        foreach (var mode in new[] { RandomizationMode.Random, RandomizationMode.Seeded })
        {
            var phase = Schedule(count, legs, mode);
            for (var leg = 1; leg <= legs; leg++)
            {
                var matches = Leg(phase, count, leg).SelectMany(round => round.Matches).ToList();
                foreach (var id in Ids(count))
                {
                    var home = matches.Count(match => Home(match) == id);
                    var away = matches.Count(match => Away(match) == id);
                    Assert.Equal(count - 1, home + away);
                    Assert.InRange(home - away, -1, 1);
                }
            }
        }
    }

    [Fact]
    public void Generate_RoundsAreImmutable()
    {
        var round = Schedule(5).Rounds[0];

        Assert.Throws<NotSupportedException>(() => ((IList<string>)round.RestingParticipantIds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Match>)round.Matches).Clear());
    }

    // Order (D3, D5 rule reused for the initial circle positions)

    [Fact]
    public void Generate_FourRankedParticipants_StartTheCircleBySeed()
    {
        var phase = RoundRobinPhase(Generate(RoundRobin(Participants(4, i => i))));

        Assert.Equal(
            [new Match("RR-R1-M1", 1, 1, new ParticipantSlot("p1"), new ParticipantSlot("p4")),
             new Match("RR-R1-M2", 1, 2, new ParticipantSlot("p2"), new ParticipantSlot("p3"))],
            phase.Rounds[0].Matches);
        Assert.Empty(phase.Rounds[0].RestingParticipantIds);
    }

    [Fact]
    public void Generate_ThreeRankedParticipants_LastSeedRestsFirst()
    {
        var phase = RoundRobinPhase(Generate(RoundRobin(Participants(3, i => i))));

        Assert.Equal(["p3"], phase.Rounds[0].RestingParticipantIds);
        Assert.Equal([new Match("RR-R1-M1", 1, 1, new ParticipantSlot("p1"), new ParticipantSlot("p2"))], phase.Rounds[0].Matches);
    }

    [Fact]
    public void Generate_SeededMode_FullyRankedIgnoresTheSeed()
    {
        var first = Generate(RoundRobin(Participants(8, i => i), RandomizationPolicy.Seeded(1)));
        var second = Generate(RoundRobin(Participants(8, i => i), RandomizationPolicy.Seeded(2)));

        Assert.Equal(Snapshot(first), Snapshot(second));
    }

    [Fact]
    public void Generate_RandomMode_IgnoresSeedRanks()
    {
        var ranked = Generate(RoundRobin(Participants(8, i => i), RandomizationPolicy.Random(11)));
        var unranked = Generate(RoundRobin(Participants(8), RandomizationPolicy.Random(11)));

        Assert.Equal(Snapshot(unranked), Snapshot(ranked));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Generate_SameRequestAndSeed_ProducesAnIdenticalDraw(RandomizationMode mode)
    {
        var request = RoundRobin(Participants(7, i => i <= 2 ? i : null), new RandomizationPolicy(mode, 123), legs: 2);

        Assert.Equal(Snapshot(Generate(request)), Snapshot(Generate(request)));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Generate_DifferentSeed_ProducesADifferentSchedule(RandomizationMode mode)
    {
        var first = Generate(RoundRobin(Participants(8), new RandomizationPolicy(mode, 123)));
        var second = Generate(RoundRobin(Participants(8), new RandomizationPolicy(mode, 124)));

        Assert.NotEqual(Snapshot(first), Snapshot(second));
    }
}
