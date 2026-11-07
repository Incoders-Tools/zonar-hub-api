using System.Numerics;
using ZonarHub.Domain.Draws;
using static ZonarHub.Tests.Domain.Draws.DrawTestData;

namespace ZonarHub.Tests.Domain.Draws;

public class GroupsThenKnockoutTests
{
    public static TheoryData<int, int> CountsAndGroupSizes
    {
        get
        {
            var data = new TheoryData<int, int>();
            for (var count = 5; count <= 33; count++)
            {
                foreach (var size in new[] { 3, 4, 5 })
                    data.Add(count, size);
            }

            return data;
        }
    }

    public static TheoryData<int, int> CountsAndGroupCounts
    {
        get
        {
            var data = new TheoryData<int, int>();
            for (var count = 5; count <= 33; count++)
            {
                foreach (var groupCount in new[] { 2, 4, 8 }.Where(groupCount => groupCount <= count / 2))
                    data.Add(count, groupCount);
            }

            return data;
        }
    }

    /// <summary>Group counts and qualifiers per group; each group gets one participant more than it qualifies.</summary>
    public static TheoryData<int, int> GroupCountsAndQualifiers
    {
        get
        {
            var data = new TheoryData<int, int>();
            for (var groupCount = 2; groupCount <= 9; groupCount++)
            {
                for (var qualifiers = 1; qualifiers <= 3; qualifiers++)
                    data.Add(groupCount, qualifiers);
            }

            return data;
        }
    }

    public static TheoryData<RandomizationMode> Modes => new() { RandomizationMode.Random, RandomizationMode.Seeded };

    private static IReadOnlyList<string> Ids(int count) => Enumerable.Range(1, count).Select(i => $"p{i}").ToList();

    private static string PairKey(Match match) =>
        string.Join('|', ParticipantIds(match).Order(StringComparer.Ordinal));

    private static IReadOnlyList<int> Sizes(Phase groupStage) => groupStage.Groups.Select(group => group.ParticipantIds.Count).ToList();

    private static GroupPositionSlot Position(string groupId, int rank) => new(groupId, rank);

    private static IReadOnlyList<Slot> FirstRoundSlots(Phase knockout) =>
        knockout.Rounds[0].Matches.SelectMany(match => new[] { match.Home, match.Away }).ToList();

    private static void AssertGroupsPartitionTheParticipants(Phase groupStage, int count)
    {
        var sizes = Sizes(groupStage);

        Assert.All(sizes, size => Assert.True(size >= 2));
        Assert.True(sizes.Max() - sizes.Min() <= 1);
        Assert.Equal(sizes.OrderDescending(), sizes);
        Assert.Equal(Ids(count).Order(), groupStage.Groups.SelectMany(group => group.ParticipantIds).Order());
    }

    // Zone sizing (S4, D7)

    [Theory]
    [MemberData(nameof(CountsAndGroupSizes))]
    public void Generate_TargetGroupSize_SizesDifferByAtMostOne_LargerGroupsFirst(int count, int groupSize)
    {
        var expectedGroups = Math.Min((count + groupSize - 1) / groupSize, count / 2);
        var qualifiers = expectedGroups == 1 ? 2 : 1;

        var groupStage = GroupStage(Generate(Groups(Participants(count), groupSize: groupSize, qualifiers: qualifiers)));

        Assert.Equal(expectedGroups, groupStage.Groups.Count);
        Assert.All(Sizes(groupStage), size => Assert.True(size <= groupSize));
        AssertGroupsPartitionTheParticipants(groupStage, count);
    }

    [Theory]
    [MemberData(nameof(CountsAndGroupCounts))]
    public void Generate_GroupCount_SizesDifferByAtMostOne_LargerGroupsFirst(int count, int groupCount)
    {
        var groupStage = GroupStage(Generate(Groups(Participants(count), groupCount: groupCount, qualifiers: 1)));

        Assert.Equal(groupCount, groupStage.Groups.Count);
        AssertGroupsPartitionTheParticipants(groupStage, count);
    }

    [Theory]
    [InlineData(10, null, 4, new[] { 4, 3, 3 })]
    [InlineData(14, null, 4, new[] { 4, 4, 3, 3 })]
    [InlineData(10, 3, null, new[] { 4, 3, 3 })]
    [InlineData(9, null, 4, new[] { 3, 3, 3 })]
    [InlineData(5, null, 4, new[] { 3, 2 })]
    [InlineData(3, null, 2, new[] { 3 })]
    [InlineData(5, null, 8, new[] { 5 })]
    [InlineData(17, 8, null, new[] { 3, 2, 2, 2, 2, 2, 2, 2 })]
    public void Generate_IndivisibleCounts_GiveTheExtraParticipantsToTheFirstGroups(int count, int? groupCount, int? groupSize, int[] expected)
    {
        var qualifiers = expected.Length == 1 ? 2 : 1;

        var groupStage = GroupStage(Generate(Groups(Participants(count), groupCount: groupCount, groupSize: groupSize, qualifiers: qualifiers)));

        Assert.Equal(expected, Sizes(groupStage));
    }

    [Theory]
    [InlineData(10, new[] { 4, 3, 3 })]
    [InlineData(16, new[] { 4, 4, 4, 4 })]
    [InlineData(3, new[] { 3 })]
    public void Generate_WithoutGroupCountOrSize_UsesGroupsOfFour(int count, int[] expected)
    {
        var request = Groups(Participants(count)) with { Options = new DrawOptions(Groups: new GroupStageOptions()) };

        Assert.Equal(expected, Sizes(GroupStage(Generate(request))));
    }

    [Fact]
    public void Generate_WithoutAnyOptions_UsesGroupsOfFourTwoQualifiersOneLegAndNoThirdPlace()
    {
        var withoutOptions = Generate(Groups(Participants(16)) with { Options = null });
        var explicitDefaults = Generate(Groups(Participants(16), groupSize: 4, qualifiers: 2, legs: 1));

        Assert.Equal(Snapshot(explicitDefaults), Snapshot(withoutOptions));
        Assert.Equal(4, Knockout(withoutOptions).Rounds[0].Matches.Count);
        Assert.Single(Knockout(withoutOptions).Rounds[^1].Matches);
    }

    [Fact]
    public void Generate_GroupIds_AreLettersThenTwoLetterIdsBeyondZ()
    {
        var groupStage = GroupStage(Generate(Groups(Participants(256), groupCount: 128, qualifiers: 1)));
        var ids = groupStage.Groups.Select(group => group.Id).ToList();

        Assert.Equal(128, ids.Count);
        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(letter => letter.ToString()), ids.Take(26));
        Assert.Equal("AA", ids[26]);
        Assert.Equal("AZ", ids[51]);
        Assert.Equal("BA", ids[52]);
        Assert.Equal("DX", ids[127]);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(Sizes(groupStage), size => Assert.Equal(2, size));
        Assert.Equal(128, Knockout(Generate(Groups(Participants(256), groupCount: 128, qualifiers: 1))).Rounds[0].Matches.Count * 2);
    }

    // Snake distribution (D7)

    [Fact]
    public void Generate_RankedParticipants_AreDistributedInSnakeOrder()
    {
        var groupStage = GroupStage(Generate(Groups(Participants(10, i => i), groupCount: 3)));

        Assert.Equal(["A", "B", "C"], groupStage.Groups.Select(group => group.Id));
        Assert.Equal(["p1", "p6", "p7", "p10"], groupStage.Groups[0].ParticipantIds);
        Assert.Equal(["p2", "p5", "p8"], groupStage.Groups[1].ParticipantIds);
        Assert.Equal(["p3", "p4", "p9"], groupStage.Groups[2].ParticipantIds);
    }

    [Fact]
    public void Generate_SnakeSkipsFullGroups_SoTheLastBackwardPassFillsTheFirstGroups()
    {
        var groupStage = GroupStage(Generate(Groups(Participants(14, i => i), groupCount: 4)));

        Assert.Equal(["p1", "p8", "p9", "p14"], groupStage.Groups[0].ParticipantIds);
        Assert.Equal(["p2", "p7", "p10", "p13"], groupStage.Groups[1].ParticipantIds);
        Assert.Equal(["p3", "p6", "p11"], groupStage.Groups[2].ParticipantIds);
        Assert.Equal(["p4", "p5", "p12"], groupStage.Groups[3].ParticipantIds);
    }

    [Theory]
    [MemberData(nameof(CountsAndGroupCounts))]
    public void Generate_TopSeeds_LandInDifferentGroups(int count, int groupCount)
    {
        var groupStage = GroupStage(Generate(Groups(Participants(count, i => i <= groupCount ? i : null), groupCount: groupCount, qualifiers: 1)));

        for (var g = 0; g < groupCount; g++)
            Assert.Equal($"p{g + 1}", groupStage.Groups[g].ParticipantIds[0]);
    }

    [Fact]
    public void Generate_RandomMode_IgnoresSeedRanks()
    {
        var ranked = Generate(Groups(Participants(12, i => i), RandomizationPolicy.Random(11), groupCount: 3));
        var unranked = Generate(Groups(Participants(12), RandomizationPolicy.Random(11), groupCount: 3));

        Assert.Equal(Snapshot(unranked), Snapshot(ranked));
    }

    // Group round robins (D4, D6, S6)

    [Theory]
    [InlineData(10, 3, 1)]
    [InlineData(10, 3, 2)]
    [InlineData(17, 4, 1)]
    [InlineData(17, 4, 2)]
    [InlineData(9, 2, 2)]
    public void Generate_EachGroupPlaysACompleteRoundRobin(int count, int groupCount, int legs)
    {
        var groupStage = GroupStage(Generate(Groups(Participants(count), groupCount: groupCount, qualifiers: 1, legs: legs)));

        foreach (var group in groupStage.Groups)
        {
            var members = group.ParticipantIds;
            var size = members.Count;
            var roundsPerLeg = size % 2 == 0 ? size - 1 : size;
            var expectedPairs = members
                .SelectMany((a, i) => members.Skip(i + 1).Select(b => string.Join('|', new[] { a, b }.Order(StringComparer.Ordinal))))
                .Order()
                .ToList();

            Assert.Equal(roundsPerLeg * legs, group.Rounds.Count);
            for (var leg = 0; leg < legs; leg++)
            {
                var pairs = group.Rounds.Skip(leg * roundsPerLeg).Take(roundsPerLeg).SelectMany(round => round.Matches).Select(PairKey).Order();
                Assert.Equal(expectedPairs, pairs);
            }

            for (var r = 0; r < group.Rounds.Count; r++)
            {
                var round = group.Rounds[r];
                Assert.Equal(r + 1, round.Number);
                Assert.Equal("matchday", round.Name);
                for (var m = 0; m < round.Matches.Count; m++)
                {
                    var match = round.Matches[m];
                    Assert.Equal($"G-{group.Id}-R{r + 1}-M{m + 1}", match.Id);
                    Assert.Equal(r + 1, match.RoundNumber);
                    Assert.Equal(m + 1, match.Number);
                    Assert.All(ParticipantIds(match), id => Assert.Contains(id, members));
                    Assert.Null(match.WinnerAdvancesTo);
                }
            }
        }
    }

    [Theory]
    [InlineData(10, 3, 1)]
    [InlineData(17, 4, 2)]
    [InlineData(11, 2, 1)]
    [InlineData(33, 8, 2)]
    public void Generate_PhaseRoundsMergeEachGroupRound_AndNobodyPlaysTwiceInAMatchday(int count, int groupCount, int legs)
    {
        var groupStage = GroupStage(Generate(Groups(Participants(count), groupCount: groupCount, qualifiers: 1, legs: legs)));

        Assert.Equal("G", groupStage.Id);
        Assert.Equal(groupStage.Groups.Max(group => group.Rounds.Count), groupStage.Rounds.Count);
        for (var r = 0; r < groupStage.Rounds.Count; r++)
        {
            var round = groupStage.Rounds[r];
            var groupRounds = groupStage.Groups.Where(group => group.Rounds.Count > r).Select(group => group.Rounds[r]).ToList();

            Assert.Equal(r + 1, round.Number);
            Assert.Equal("matchday", round.Name);
            Assert.Equal(groupRounds.SelectMany(groupRound => groupRound.Matches), round.Matches);
            Assert.Equal(groupRounds.SelectMany(groupRound => groupRound.RestingParticipantIds), round.RestingParticipantIds);

            var playing = round.Matches.SelectMany(ParticipantIds).ToList();
            Assert.Equal(playing.Count, playing.Distinct().Count());
            Assert.Empty(playing.Intersect(round.RestingParticipantIds));
        }

        Assert.Equal(groupStage.Groups.SelectMany(group => group.Matches).Select(match => match.Id).Order(), groupStage.Matches.Select(match => match.Id).Order());
    }

    [Fact]
    public void Generate_GroupsAreImmutable()
    {
        var groupStage = GroupStage(Generate(Groups(Participants(10), groupCount: 3)));

        Assert.Throws<NotSupportedException>(() => ((IList<Group>)groupStage.Groups).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)groupStage.Groups[0].ParticipantIds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Round>)groupStage.Groups[0].Rounds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Round>)groupStage.Rounds).Clear());
    }

    // Knockout from groups (D7, S5)

    [Fact]
    public void Generate_DrawHasTheGroupStageThenTheKnockout()
    {
        var draw = Generate(Groups(Participants(16), groupCount: 4));

        Assert.Equal(DrawFormat.GroupsThenKnockout, draw.Format);
        Assert.Equal([PhaseKind.GroupStage, PhaseKind.Knockout], draw.Phases.Select(phase => phase.Kind));
        Assert.Equal(["G", "KO"], draw.Phases.Select(phase => phase.Id));
        Assert.Empty(Knockout(draw).Groups);
    }

    [Theory]
    [MemberData(nameof(GroupCountsAndQualifiers))]
    public void Generate_KnockoutEntrantsAreEveryQualifyingPosition_ByesGoToTheBestPlaced(int groupCount, int qualifiers)
    {
        var draw = Generate(Groups(Participants(groupCount * (qualifiers + 1)), groupCount: groupCount, qualifiers: qualifiers));
        var groupIds = GroupStage(draw).Groups.Select(group => group.Id).ToList();
        var knockout = Knockout(draw);
        var entrants = groupIds.Count * qualifiers;
        var size = (int)BitOperations.RoundUpToPowerOf2((uint)entrants);
        var slots = FirstRoundSlots(knockout);

        Assert.Equal(size / 2, knockout.Rounds[0].Matches.Count);
        Assert.Equal(size - entrants, slots.Count(slot => slot is ByeSlot));
        Assert.Equal(
            groupIds.SelectMany(groupId => Enumerable.Range(1, qualifiers).Select(rank => (Slot)Position(groupId, rank))).OrderBy(slot => slot.ToString()),
            slots.Where(slot => slot is not ByeSlot).OrderBy(slot => slot.ToString()));

        var byeRanks = knockout.Rounds[0].Matches.Where(match => match.IsBye).Select(match => ((GroupPositionSlot)match.Home).Rank).ToList();
        var playingRanks = knockout.Rounds[0].Matches.Where(match => !match.IsBye)
            .SelectMany(match => new[] { match.Home, match.Away }).Cast<GroupPositionSlot>().Select(slot => slot.Rank).ToList();
        if (byeRanks.Count > 0)
            Assert.True(byeRanks.Max() <= playingRanks.Min());

        var winnerByes = knockout.Rounds[0].Matches.Where(match => match.IsBye && ((GroupPositionSlot)match.Home).Rank == 1)
            .Select(match => ((GroupPositionSlot)match.Home).GroupId).Order(StringComparer.Ordinal);
        Assert.Equal(groupIds.Take(Math.Min(size - entrants, groupIds.Count)), winnerByes);
    }

    [Theory]
    [MemberData(nameof(GroupCountsAndQualifiers))]
    public void Generate_FirstRoundNeverPairsTwoQualifiersOfTheSameGroup(int groupCount, int qualifiers)
    {
        foreach (var mode in new[] { RandomizationMode.Random, RandomizationMode.Seeded })
        {
            var draw = Generate(Groups(Participants(groupCount * (qualifiers + 1)), new RandomizationPolicy(mode, 7), groupCount: groupCount, qualifiers: qualifiers));

            Assert.All(Knockout(draw).Rounds[0].Matches.Where(match => !match.IsBye), match =>
                Assert.NotEqual(((GroupPositionSlot)match.Home).GroupId, ((GroupPositionSlot)match.Away).GroupId));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void Generate_TwoQualifiersPerPowerOfTwoGroups_PairWinnersWithRunnersUpOfOtherGroups(int groupCount)
    {
        var draw = Generate(Groups(Participants(groupCount * 4), groupCount: groupCount));

        Assert.All(Knockout(draw).Rounds[0].Matches, match =>
        {
            var home = Assert.IsType<GroupPositionSlot>(match.Home);
            var away = Assert.IsType<GroupPositionSlot>(match.Away);
            Assert.Equal(1, home.Rank);
            Assert.Equal(2, away.Rank);
            Assert.NotEqual(home.GroupId, away.GroupId);
        });
    }

    [Fact]
    public void Generate_FourGroupsOfTwoQualifiers_CrossWinnersAndRunnersUp()
    {
        var knockout = Knockout(Generate(Groups(Participants(16), groupCount: 4)));

        Assert.Equal(
            [(Position("A", 1), Position("D", 2)),
             (Position("D", 1), Position("A", 2)),
             (Position("B", 1), Position("C", 2)),
             (Position("C", 1), Position("B", 2))],
            knockout.Rounds[0].Matches.Select(match => ((GroupPositionSlot)match.Home, (GroupPositionSlot)match.Away)));
    }

    [Fact]
    public void Generate_ThreeGroupsOfTwoQualifiers_ByesForTwoWinners_AndRunnersUpRotatedAwayFromTheirOwnWinner()
    {
        var knockout = Knockout(Generate(Groups(Participants(12), groupCount: 3)));

        Assert.Equal(
            [(Position("A", 1), (Slot)Slot.Bye),
             (Position("B", 2), Position("C", 2)),
             (Position("B", 1), Slot.Bye),
             (Position("C", 1), Position("A", 2))],
            knockout.Rounds[0].Matches.Select(match => ((GroupPositionSlot)match.Home, match.Away)));
        Assert.Equal(Position("A", 1), knockout.Rounds[1].Matches[0].Home);
        Assert.Equal(new WinnerOfSlot("KO-R1-M2"), knockout.Rounds[1].Matches[0].Away);
    }

    [Theory]
    [MemberData(nameof(GroupCountsAndQualifiers))]
    public void Generate_EveryEntrantCanReachTheFinal(int groupCount, int qualifiers)
    {
        var knockout = Knockout(Generate(Groups(Participants(groupCount * (qualifiers + 1)), groupCount: groupCount, qualifiers: qualifiers)));
        var byId = knockout.Matches.ToDictionary(match => match.Id);
        var final = knockout.Rounds[^1].Matches[0];

        foreach (var match in knockout.Rounds[0].Matches)
        {
            var current = match;
            while (current.WinnerAdvancesTo is not null)
                current = byId[current.WinnerAdvancesTo];

            Assert.Equal(final, current);
        }

        Assert.Equal("final", knockout.Rounds[^1].Name);
    }

    [Fact]
    public void Generate_GroupsThirdPlace_IsFedByTheSemifinalLosers()
    {
        var knockout = Knockout(Generate(Groups(Participants(16), groupCount: 4, thirdPlaceMatch: true)));
        var final = knockout.Rounds[^1];
        var semifinals = knockout.Rounds[^2].Matches;

        Assert.Equal(2, final.Matches.Count);
        Assert.Equal(new Match("KO-R3-M2", 3, 2, new LoserOfSlot(semifinals[0].Id), new LoserOfSlot(semifinals[1].Id)), final.Matches[1]);
        Assert.All(semifinals, semifinal => Assert.Equal("KO-R3-M2", semifinal.LoserAdvancesTo));
    }

    [Fact]
    public void Generate_KnockoutDoesNotDependOnTheGroupDistribution()
    {
        var first = Knockout(Generate(Groups(Participants(16), RandomizationPolicy.Random(1), groupCount: 4)));
        var second = Knockout(Generate(Groups(Participants(16), RandomizationPolicy.Random(2), groupCount: 4)));

        Assert.Equal(first.Matches, second.Matches);
    }

    // Determinism (S3, D3)

    [Theory]
    [MemberData(nameof(Modes))]
    public void Generate_SameRequestAndSeed_ProducesAnIdenticalDraw(RandomizationMode mode)
    {
        var request = Groups(Participants(19, i => i <= 3 ? i : null), new RandomizationPolicy(mode, 123), groupCount: 4, legs: 2, thirdPlaceMatch: true);

        Assert.Equal(Snapshot(Generate(request)), Snapshot(Generate(request)));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Generate_DifferentSeed_ProducesDifferentGroups(RandomizationMode mode)
    {
        var first = GroupStage(Generate(Groups(Participants(16), new RandomizationPolicy(mode, 123), groupCount: 4)));
        var second = GroupStage(Generate(Groups(Participants(16), new RandomizationPolicy(mode, 124), groupCount: 4)));

        Assert.NotEqual(first.Groups.SelectMany(group => group.ParticipantIds), second.Groups.SelectMany(group => group.ParticipantIds));
    }

    [Fact]
    public void Generate_SeededMode_FullyRankedIgnoresTheSeed()
    {
        var first = Generate(Groups(Participants(12, i => i), RandomizationPolicy.Seeded(1), groupCount: 3));
        var second = Generate(Groups(Participants(12, i => i), RandomizationPolicy.Seeded(2), groupCount: 3));

        Assert.Equal(Snapshot(first), Snapshot(second));
    }
}
