using System.Numerics;
using ZonarHub.Domain.Draws;
using static ZonarHub.Tests.Domain.Draws.DrawTestData;

namespace ZonarHub.Tests.Domain.Draws;

public class SingleEliminationTests
{
    public static TheoryData<int> Counts => new() { 2, 3, 4, 5, 7, 8, 9, 16, 17, 256 };

    public static TheoryData<RandomizationMode> Modes => new() { RandomizationMode.Random, RandomizationMode.Seeded };

    private static Phase Bracket(int count, RandomizationMode mode = RandomizationMode.Seeded, ulong seed = 2026) =>
        Knockout(Generate(SingleElimination(Participants(count), new RandomizationPolicy(mode, seed))));

    private static int BracketSize(int count) => (int)BitOperations.RoundUpToPowerOf2((uint)count);

    [Theory]
    [MemberData(nameof(Counts))]
    public void Generate_RoundsAndMatchesPerRound_FollowTheBracketSize(int count)
    {
        var knockout = Bracket(count);
        var size = BracketSize(count);

        Assert.Equal("KO", knockout.Id);
        Assert.Equal(BitOperations.Log2((uint)size), knockout.Rounds.Count);
        for (var r = 0; r < knockout.Rounds.Count; r++)
        {
            var round = knockout.Rounds[r];
            Assert.Equal(r + 1, round.Number);
            Assert.Equal(size >> (r + 1), round.Matches.Count);
            for (var m = 0; m < round.Matches.Count; m++)
            {
                var match = round.Matches[m];
                Assert.Equal($"KO-R{r + 1}-M{m + 1}", match.Id);
                Assert.Equal(r + 1, match.RoundNumber);
                Assert.Equal(m + 1, match.Number);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void Generate_RoundsAreNamedUpToTheFinal(int count)
    {
        var names = Bracket(count).Rounds.Select(round => round.Name).Reverse().ToList();
        string[] expected = ["final", "semifinal", "quarterfinal", "round_of_16", "round_of_32", "round_of_64", "round_of_128", "round_of_256"];

        Assert.Equal(expected.Take(names.Count), names);
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void Generate_ByesFillTheBracketAndNeverMeetEachOther(int count)
    {
        foreach (var mode in new[] { RandomizationMode.Random, RandomizationMode.Seeded })
        {
            var knockout = Bracket(count, mode);
            var firstRound = knockout.Rounds[0].Matches;

            Assert.Equal(BracketSize(count) - count, firstRound.Count(match => match.IsBye));
            Assert.Equal(BracketSize(count) - count, firstRound.Sum(match => (match.Home is ByeSlot ? 1 : 0) + (match.Away is ByeSlot ? 1 : 0)));
            Assert.DoesNotContain(knockout.Matches, match => match.Home is ByeSlot && match.Away is ByeSlot);
            Assert.DoesNotContain(knockout.Rounds.Skip(1).SelectMany(round => round.Matches), match => match.IsBye);
        }
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void Generate_EveryParticipantAppearsExactlyOnceInTheFirstRound(int count)
    {
        var knockout = Bracket(count);

        var firstRoundIds = knockout.Rounds[0].Matches.SelectMany(ParticipantIds).ToList();

        Assert.Equal(Enumerable.Range(1, count).Select(i => $"p{i}").Order(), firstRoundIds.Order());
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void Generate_NoParticipantPlaysTwiceInARound(int count)
    {
        foreach (var round in Bracket(count).Rounds)
        {
            var ids = round.Matches.SelectMany(ParticipantIds).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());

            var feeders = round.Matches
                .SelectMany(match => new[] { match.Home, match.Away })
                .OfType<WinnerOfSlot>()
                .Select(slot => slot.MatchId)
                .ToList();
            Assert.Equal(feeders.Count, feeders.Distinct().Count());
        }
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void Generate_EveryMatchAdvancesToExactlyOneNextMatch_AndTheFinalNowhere(int count)
    {
        var knockout = Bracket(count);
        var byId = knockout.Matches.ToDictionary(match => match.Id);
        var final = knockout.Rounds[^1].Matches.Single();

        Assert.Null(final.WinnerAdvancesTo);
        foreach (var match in knockout.Matches.Where(match => match != final))
        {
            Assert.NotNull(match.WinnerAdvancesTo);
            var next = byId[match.WinnerAdvancesTo!];
            Assert.Equal(match.RoundNumber + 1, next.RoundNumber);
            Assert.Equal((match.Number + 1) / 2, next.Number);

            var slot = match.Number % 2 == 1 ? next.Home : next.Away;
            if (match.IsBye)
            {
                var advancing = Assert.Single(ParticipantIds(match));
                Assert.Equal(new ParticipantSlot(advancing), slot);
            }
            else
            {
                Assert.Equal(new WinnerOfSlot(match.Id), slot);
            }

            Assert.Null(match.LoserAdvancesTo);
        }

        var referenced = knockout.Matches
            .SelectMany(match => new[] { match.Home, match.Away })
            .OfType<WinnerOfSlot>()
            .Select(slot => slot.MatchId);
        Assert.Equal(knockout.Matches.Where(match => match != final && !match.IsBye).Select(match => match.Id).Order(), referenced.Order());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(17)]
    public void Generate_ByeParticipantIsResolvedInTheSecondRound(int count)
    {
        var knockout = Bracket(count);
        var secondRoundIds = knockout.Rounds[1].Matches.SelectMany(ParticipantIds).ToHashSet();

        Assert.Equal(ByeRecipients(knockout), secondRoundIds);
        Assert.Equal(BracketSize(count) - count, secondRoundIds.Count);
    }

    [Fact]
    public void Generate_DrawCarriesRequestMetadataAndAnImmutableParticipantCopy()
    {
        var members = new List<string> { "Ana", "Bea" };
        var participants = new List<DrawParticipant>
        {
            new("t1", "Team 1", members, separationKey: "club-a"),
            new("t2", "Team 2"),
        };

        var draw = Generate(SingleElimination(participants, RandomizationPolicy.Random(77), participationFormat: ParticipationFormat.Pair));
        members.Add("Carla");
        participants.Add(new DrawParticipant("t3", "Team 3"));

        Assert.Equal(DrawFormat.SingleElimination, draw.Format);
        Assert.Equal(ParticipationFormat.Pair, draw.ParticipationFormat);
        Assert.Equal(RandomizationMode.Random, draw.RandomizationMode);
        Assert.Equal(77UL, draw.Seed);
        Assert.Equal(["t1", "t2"], draw.Participants.Select(p => p.Id));
        Assert.Equal(["Ana", "Bea"], draw.Participants[0].Members);
        Assert.Equal("club-a", draw.Participants[0].SeparationKey);
        Assert.Empty(draw.Participants[1].Members);
        Assert.Throws<NotSupportedException>(() => ((IList<DrawParticipant>)draw.Participants).Add(participants[2]));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)draw.Participants[0].Members).Add("Carla"));
        Assert.Throws<NotSupportedException>(() => ((IList<Match>)Knockout(draw).Rounds[0].Matches).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Round>)Knockout(draw).Rounds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Phase>)draw.Phases).Clear());
    }

    [Theory]
    [MemberData(nameof(Counts))]
    public void Generate_KnockoutRoundsHaveNoRestingParticipants_ByesStayByeMatches(int count)
    {
        Assert.All(Bracket(count).Rounds, round => Assert.Empty(round.RestingParticipantIds));
    }

    [Fact]
    public void Generate_TwoParticipants_IsASingleFinal()
    {
        var knockout = Bracket(2);

        var final = Assert.Single(Assert.Single(knockout.Rounds).Matches);
        Assert.Equal(["p1", "p2"], ParticipantIds(final).Order());
        Assert.False(final.IsBye);
    }

    // Seeding (D5)

    [Fact]
    public void Generate_FourSeeds_UseStandardPositions()
    {
        var knockout = Knockout(Generate(SingleElimination(Participants(4, i => i))));

        Assert.Equal(
            [new Match("KO-R1-M1", 1, 1, new ParticipantSlot("p1"), new ParticipantSlot("p4"), "KO-R2-M1"),
             new Match("KO-R1-M2", 1, 2, new ParticipantSlot("p2"), new ParticipantSlot("p3"), "KO-R2-M1")],
            knockout.Rounds[0].Matches);
    }

    [Fact]
    public void Generate_EightSeeds_UseStandardPositions()
    {
        var knockout = Knockout(Generate(SingleElimination(Participants(8, i => i))));

        var pairs = knockout.Rounds[0].Matches.Select(match => (((ParticipantSlot)match.Home).ParticipantId, ((ParticipantSlot)match.Away).ParticipantId));

        Assert.Equal([("p1", "p8"), ("p4", "p5"), ("p2", "p7"), ("p3", "p6")], pairs);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(13)]
    [InlineData(32)]
    public void Generate_SeedsOneAndTwoCanOnlyMeetInTheFinal_AndTopFourAreInDifferentQuarters(int count)
    {
        var knockout = Knockout(Generate(SingleElimination(Participants(count, i => i <= 4 ? i : null))));
        var firstRoundMatches = knockout.Rounds[0].Matches.Count;

        var half = (string id) => FirstRoundMatchIndex(knockout, id) / (firstRoundMatches / 2);
        var quarter = (string id) => FirstRoundMatchIndex(knockout, id) / (firstRoundMatches / 4);

        Assert.NotEqual(half("p1"), half("p2"));
        Assert.Equal(4, new[] { "p1", "p2", "p3", "p4" }.Select(quarter).Distinct().Count());
    }

    [Theory]
    [InlineData(5, new[] { "p1", "p2", "p3" })]
    [InlineData(6, new[] { "p1", "p2" })]
    [InlineData(14, new[] { "p1", "p2" })]
    public void Generate_ByesGoToTheTopSeedsFirst(int count, string[] expectedByes)
    {
        var knockout = Knockout(Generate(SingleElimination(Participants(count, i => i <= 3 ? i : null))));

        Assert.Equal(expectedByes, ByeRecipients(knockout).Order());
    }

    [Fact]
    public void Generate_MoreByesThanSeeds_GiveTheRemainingByesToUnseededParticipants()
    {
        var knockout = Knockout(Generate(SingleElimination(Participants(9, i => i == 1 ? 1 : null))));
        var byes = ByeRecipients(knockout);

        Assert.Equal(7, byes.Count);
        Assert.Contains("p1", byes);
    }

    [Fact]
    public void Generate_SeedRanksWithGaps_AreOrderedByRank()
    {
        var knockout = Knockout(Generate(SingleElimination(Participants(7, i => i switch { 6 => 10, 7 => 20, _ => null }))));

        Assert.Equal(["p6"], ByeRecipients(knockout));
        Assert.NotEqual(FirstRoundMatchIndex(knockout, "p6") / 2, FirstRoundMatchIndex(knockout, "p7") / 2);
    }

    [Fact]
    public void Generate_RandomMode_IgnoresSeedRanks()
    {
        var ranked = Generate(SingleElimination(Participants(16, i => i), RandomizationPolicy.Random(11)));
        var unranked = Generate(SingleElimination(Participants(16), RandomizationPolicy.Random(11)));

        Assert.Equal(Snapshot(unranked), Snapshot(ranked));
    }

    [Fact]
    public void Generate_RandomMode_ByeRecipientsDependOnTheShuffle()
    {
        var recipients = Enumerable.Range(1, 20)
            .Select(seed => string.Join(',', ByeRecipients(Bracket(5, RandomizationMode.Random, (ulong)seed)).Order()))
            .ToHashSet();

        Assert.True(recipients.Count > 1);
    }

    // Determinism (D3)

    [Theory]
    [MemberData(nameof(Modes))]
    public void Generate_SameRequestAndSeed_ProducesAnIdenticalDraw(RandomizationMode mode)
    {
        var request = SingleElimination(Participants(17, i => i <= 2 ? i : null), new RandomizationPolicy(mode, 123), thirdPlaceMatch: true);

        var first = Generate(request);
        var second = Generate(request);

        Assert.Equal(Snapshot(first), Snapshot(second));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Generate_DifferentSeed_ProducesADifferentOrder(RandomizationMode mode)
    {
        var first = Bracket(16, mode, 123);
        var second = Bracket(16, mode, 124);

        Assert.NotEqual(first.Rounds[0].Matches, second.Rounds[0].Matches);
    }

    // Third-place match

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(16)]
    public void Generate_ThirdPlaceRequested_IsFedByTheSemifinalLosers(int count)
    {
        var knockout = Knockout(Generate(SingleElimination(Participants(count), thirdPlaceMatch: true)));
        var finalRound = knockout.Rounds[^1];
        var semifinals = knockout.Rounds[^2].Matches;

        Assert.Equal("final", finalRound.Name);
        Assert.Equal(2, finalRound.Matches.Count);
        var final = finalRound.Matches[0];
        var thirdPlace = finalRound.Matches[1];

        Assert.Equal(new WinnerOfSlot(semifinals[0].Id), final.Home);
        Assert.Equal(new WinnerOfSlot(semifinals[1].Id), final.Away);
        Assert.Equal($"KO-R{finalRound.Number}-M2", thirdPlace.Id);
        Assert.Equal(new LoserOfSlot(semifinals[0].Id), thirdPlace.Home);
        Assert.Equal(new LoserOfSlot(semifinals[1].Id), thirdPlace.Away);
        Assert.Null(thirdPlace.WinnerAdvancesTo);
        Assert.Null(thirdPlace.LoserAdvancesTo);
        Assert.All(semifinals, semifinal =>
        {
            Assert.Equal(final.Id, semifinal.WinnerAdvancesTo);
            Assert.Equal(thirdPlace.Id, semifinal.LoserAdvancesTo);
        });
    }

    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    public void Generate_ThirdPlaceNotRequested_HasNoLoserPaths(int count)
    {
        var knockout = Bracket(count);

        Assert.Single(knockout.Rounds[^1].Matches);
        Assert.DoesNotContain(knockout.Matches, match => match.Home is LoserOfSlot || match.Away is LoserOfSlot);
        Assert.All(knockout.Matches, match => Assert.Null(match.LoserAdvancesTo));
    }
}
