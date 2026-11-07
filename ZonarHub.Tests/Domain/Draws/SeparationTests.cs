using ZonarHub.Domain.Draws;
using static ZonarHub.Tests.Domain.Draws.DrawTestData;

namespace ZonarHub.Tests.Domain.Draws;

/// <summary>
/// Separation keys (D8): conditioned randomization that keeps participants sharing a key apart when possible.
/// </summary>
public class SeparationTests
{
    public static TheoryData<RandomizationMode, ulong> ModesAndSeeds
    {
        get
        {
            var data = new TheoryData<RandomizationMode, ulong>();
            foreach (var mode in new[] { RandomizationMode.Random, RandomizationMode.Seeded })
            {
                for (var seed = 1UL; seed <= 40; seed++)
                    data.Add(mode, seed);
            }

            return data;
        }
    }

    public static TheoryData<ulong> Seeds => new(Enumerable.Range(1, 40).Select(seed => (ulong)seed));

    public static TheoryData<DrawFormat, RandomizationMode> FormatsAndModes
    {
        get
        {
            var data = new TheoryData<DrawFormat, RandomizationMode>();
            foreach (var format in new[] { DrawFormat.SingleElimination, DrawFormat.RoundRobin, DrawFormat.GroupsThenKnockout })
            {
                data.Add(format, RandomizationMode.Random);
                data.Add(format, RandomizationMode.Seeded);
            }

            return data;
        }
    }

    /// <summary>Participants <c>p1..pN</c> with the given separation keys (one per participant).</summary>
    private static IReadOnlyList<DrawParticipant> Keyed(IReadOnlyList<string?> keys, Func<int, int?>? seedRank = null) =>
        keys.Select((key, index) => new DrawParticipant($"p{index + 1}", $"Player {index + 1}", seedRank: seedRank?.Invoke(index + 1), separationKey: key))
            .ToList();

    /// <summary><paramref name="clubs"/> clubs of <paramref name="perClub"/> members, interleaved: p1 club-1, p2 club-2, ...</summary>
    private static IReadOnlyList<string?> Clubs(int clubs, int perClub) =>
        Enumerable.Range(0, clubs * perClub).Select(index => (string?)$"club-{index % clubs + 1}").ToList();

    private static Dictionary<string, string?> KeyOf(Draw draw) =>
        draw.Participants.ToDictionary(participant => participant.Id, participant => participant.SeparationKey?.Trim().ToUpperInvariant());

    private static IEnumerable<Match> FirstRoundPlayed(Draw draw) =>
        Knockout(draw).Rounds[0].Matches.Where(match => !match.IsBye);

    private static int FirstRoundClashes(Draw draw)
    {
        var keyOf = KeyOf(draw);
        return FirstRoundPlayed(draw).Count(match =>
        {
            var ids = ParticipantIds(match).ToList();
            return keyOf[ids[0]] is not null && keyOf[ids[0]] == keyOf[ids[1]];
        });
    }

    private static int SameGroupKeyRepeats(Group group, Dictionary<string, string?> keyOf) =>
        group.ParticipantIds.Where(id => keyOf[id] is not null).GroupBy(id => keyOf[id]).Count(members => members.Count() > 1);

    private static DrawRequest Request(DrawFormat format, IReadOnlyList<DrawParticipant> participants, RandomizationMode mode, ulong seed) =>
        format switch
        {
            DrawFormat.SingleElimination => SingleElimination(participants, new RandomizationPolicy(mode, seed)),
            DrawFormat.RoundRobin => RoundRobin(participants, new RandomizationPolicy(mode, seed)),
            _ => Groups(participants, new RandomizationPolicy(mode, seed), groupCount: 3),
        };

    /// <summary>Compact text of the placement: first knockout round, round-robin rounds and group members.</summary>
    private static string Fingerprint(Draw draw) =>
        string.Join(' ', draw.Phases.Select(phase => phase.Kind switch
        {
            PhaseKind.Knockout => $"{phase.Id}[{string.Join(';', phase.Rounds[0].Matches.Select(match => $"{SlotText(match.Home)}-{SlotText(match.Away)}"))}]",
            PhaseKind.RoundRobin => $"{phase.Id}[{string.Join('/', phase.Rounds.Select(round => string.Join(';', round.Matches.Select(match => $"{SlotText(match.Home)}-{SlotText(match.Away)}"))))}]",
            _ => $"{phase.Id}[{string.Join(';', phase.Groups.Select(group => $"{group.Id}={string.Join(',', group.ParticipantIds)}"))}]",
        }));

    private static string SlotText(Slot slot) => slot switch
    {
        ParticipantSlot participant => participant.ParticipantId,
        GroupPositionSlot position => $"{position.GroupId}{position.Rank}",
        _ => "bye",
    };

    // Single elimination

    [Theory]
    [MemberData(nameof(ModesAndSeeds))]
    public void SingleElimination_EightParticipantsOfFourClubs_NeverMeetTheirClubInTheFirstRound(RandomizationMode mode, ulong seed)
    {
        var draw = Generate(SingleElimination(Keyed(Clubs(4, 2)), new RandomizationPolicy(mode, seed)));

        Assert.Equal(0, FirstRoundClashes(draw));
        Assert.Empty(draw.SeparationConflicts);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $"p{i}").Order(), Knockout(draw).Rounds[0].Matches.SelectMany(ParticipantIds).Order());
    }

    [Fact]
    public void SingleElimination_FourParticipantsOfOneClub_SucceedsAndReportsEveryFirstRoundMatch()
    {
        var draw = Generate(SingleElimination(Keyed([" Club A", "club a", "CLUB A ", "Club A"]), RandomizationPolicy.Seeded(5)));
        var firstRound = Knockout(draw).Rounds[0].Matches;

        Assert.Equal(2, draw.SeparationConflicts.Count);
        for (var m = 0; m < firstRound.Count; m++)
        {
            var conflict = draw.SeparationConflicts[m];
            var homeId = ((ParticipantSlot)firstRound[m].Home).ParticipantId;
            Assert.Equal(SeparationConflictKind.FirstRoundMatch, conflict.Kind);
            Assert.Equal($"KO-R1-M{m + 1}", conflict.LocationId);
            Assert.Equal(ParticipantIds(firstRound[m]), conflict.ParticipantIds);
            Assert.Equal(draw.Participants.Single(participant => participant.Id == homeId).SeparationKey!.Trim(), conflict.Key);
        }
    }

    [Theory]
    [MemberData(nameof(ModesAndSeeds))]
    public void SingleElimination_OneClubWithSixOfEight_ReportsOnlyTheUnavoidableClashes(RandomizationMode mode, ulong seed)
    {
        var draw = Generate(SingleElimination(Keyed(["A", "A", "A", "B", "A", "A", "C", "A"]), new RandomizationPolicy(mode, seed)));

        Assert.Equal(2, FirstRoundClashes(draw));
        Assert.Equal(2, draw.SeparationConflicts.Count);
        Assert.All(draw.SeparationConflicts, conflict =>
        {
            Assert.Equal("A", conflict.Key);
            Assert.Equal(SeparationConflictKind.FirstRoundMatch, conflict.Kind);
            Assert.Equal(2, conflict.ParticipantIds.Count);
        });
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void SingleElimination_SeededParticipantsKeepTheirStandardPositions(ulong seed)
    {
        // Seeds 1-4 belong to clubs A-D; the unseeded p5-p8 belong to the same clubs.
        var participants = Keyed(["A", "B", "C", "D", "A", "B", "C", "D"], i => i <= 4 ? i : null);

        var draw = Generate(SingleElimination(participants, RandomizationPolicy.Seeded(seed)));
        var homes = Knockout(draw).Rounds[0].Matches.Select(match => ((ParticipantSlot)match.Home).ParticipantId);

        Assert.Equal(["p1", "p4", "p2", "p3"], homes);
        Assert.Equal(0, FirstRoundClashes(draw));
        Assert.Empty(draw.SeparationConflicts);
    }

    [Fact]
    public void SingleElimination_TwoSeedsOfTheSameClubFacingEachOther_StayAndAreReported()
    {
        var draw = Generate(SingleElimination(Keyed(["A", "B", "C", "A"], i => i), RandomizationPolicy.Seeded(9)));

        Assert.Equal(
            [new Match("KO-R1-M1", 1, 1, new ParticipantSlot("p1"), new ParticipantSlot("p4"), "KO-R2-M1"),
             new Match("KO-R1-M2", 1, 2, new ParticipantSlot("p2"), new ParticipantSlot("p3"), "KO-R2-M1")],
            Knockout(draw).Rounds[0].Matches);
        var conflict = Assert.Single(draw.SeparationConflicts);
        Assert.Equal(("A", SeparationConflictKind.FirstRoundMatch, "KO-R1-M1"), (conflict.Key, conflict.Kind, conflict.LocationId));
        Assert.Equal(["p1", "p4"], conflict.ParticipantIds);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void SingleElimination_ByesStayWithTheTopSeeds(ulong seed)
    {
        // Six entrants: the two byes go to seeds p1 and p2; the unseeded A, A, B, B must be crossed.
        var participants = Keyed(["A", "B", "A", "A", "B", "B"], i => i <= 2 ? i : null);

        var draw = Generate(SingleElimination(participants, RandomizationPolicy.Seeded(seed)));

        Assert.Equal(["p1", "p2"], ByeRecipients(Knockout(draw)).Order());
        Assert.Equal(0, FirstRoundClashes(draw));
        Assert.Empty(draw.SeparationConflicts);
    }

    [Theory]
    [MemberData(nameof(ModesAndSeeds))]
    public void SingleElimination_KeysAreTrimmedAndCaseInsensitive(RandomizationMode mode, ulong seed)
    {
        var draw = Generate(SingleElimination(Keyed(["Club A", " club a ", "CLUB B", "club b\t"]), new RandomizationPolicy(mode, seed)));
        var keyOf = KeyOf(draw);

        Assert.All(FirstRoundPlayed(draw), match =>
        {
            var ids = ParticipantIds(match).ToList();
            Assert.NotEqual(keyOf[ids[0]], keyOf[ids[1]]);
        });
        Assert.Empty(draw.SeparationConflicts);
    }

    [Fact]
    public void SingleElimination_BlankKeys_AreNoKey()
    {
        var draw = Generate(SingleElimination(Keyed(["", "  ", null, "\t"]), RandomizationPolicy.Seeded(3)));

        Assert.Empty(draw.SeparationConflicts);
    }

    [Theory]
    [MemberData(nameof(ModesAndSeeds))]
    public void SingleElimination_SameRequestAndSeed_ProducesTheSameSeparatedDraw(RandomizationMode mode, ulong seed)
    {
        var request = SingleElimination(Keyed(["A", "A", "A", "B", "A", "C", "C", "B", "A", "D", "B"], i => i == 1 ? 1 : null), new RandomizationPolicy(mode, seed));

        var first = Generate(request);
        var second = Generate(request);

        Assert.Equal(Snapshot(first), Snapshot(second));
        Assert.Equal(
            first.SeparationConflicts.Select(conflict => (conflict.Key, string.Join(',', conflict.ParticipantIds), conflict.Kind, conflict.LocationId)),
            second.SeparationConflicts.Select(conflict => (conflict.Key, string.Join(',', conflict.ParticipantIds), conflict.Kind, conflict.LocationId)));
    }

    // Groups then knockout

    [Theory]
    [MemberData(nameof(ModesAndSeeds))]
    public void Groups_TwelveParticipantsOfFourClubsInFourGroups_NoGroupRepeatsAClub(RandomizationMode mode, ulong seed)
    {
        var draw = Generate(Groups(Keyed(Clubs(4, 3)), new RandomizationPolicy(mode, seed), groupCount: 4, qualifiers: 1));
        var groupStage = GroupStage(draw);
        var keyOf = KeyOf(draw);

        Assert.Equal([3, 3, 3, 3], groupStage.Groups.Select(group => group.ParticipantIds.Count));
        Assert.All(groupStage.Groups, group => Assert.Equal(0, SameGroupKeyRepeats(group, keyOf)));
        Assert.Empty(draw.SeparationConflicts);
        Assert.Equal(Enumerable.Range(1, 12).Select(i => $"p{i}").Order(), groupStage.Groups.SelectMany(group => group.ParticipantIds).Order());
    }

    [Theory]
    [MemberData(nameof(ModesAndSeeds))]
    public void Groups_ClubLargerThanTheGroupCount_ReportsTheMinimalSameGroupConflicts(RandomizationMode mode, ulong seed)
    {
        // Club A has six of twelve participants across four groups: two groups must hold two of them.
        IReadOnlyList<string?> keys = ["A", null, "A", "B", "A", null, "A", "C", "A", "D", "A", null];

        var draw = Generate(Groups(Keyed(keys), new RandomizationPolicy(mode, seed), groupCount: 4, qualifiers: 1));
        var groupStage = GroupStage(draw);
        var keyOf = KeyOf(draw);

        Assert.Equal([3, 3, 3, 3], groupStage.Groups.Select(group => group.ParticipantIds.Count));
        Assert.Equal([1, 1, 2, 2], groupStage.Groups.Select(group => group.ParticipantIds.Count(id => keyOf[id] == "A")).Order());
        Assert.Equal(2, draw.SeparationConflicts.Count);
        Assert.All(draw.SeparationConflicts, conflict =>
        {
            var group = groupStage.Groups.Single(group => group.Id == conflict.LocationId);
            Assert.Equal("A", conflict.Key);
            Assert.Equal(SeparationConflictKind.SameGroup, conflict.Kind);
            Assert.Equal(group.ParticipantIds.Where(id => keyOf[id] == "A"), conflict.ParticipantIds);
        });
        Assert.Equal(
            groupStage.Groups.Where(group => SameGroupKeyRepeats(group, keyOf) > 0).Select(group => group.Id),
            draw.SeparationConflicts.Select(conflict => conflict.LocationId));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Groups_SeededHeadsStayInPlace_AndTheUnseededAreSeparated(ulong seed)
    {
        var draw = Generate(Groups(Keyed(Clubs(4, 3), i => i <= 4 ? i : null), RandomizationPolicy.Seeded(seed), groupCount: 4, qualifiers: 1));
        var groupStage = GroupStage(draw);
        var keyOf = KeyOf(draw);

        Assert.Equal(["p1", "p2", "p3", "p4"], groupStage.Groups.Select(group => group.ParticipantIds[0]));
        Assert.All(groupStage.Groups, group => Assert.Equal(0, SameGroupKeyRepeats(group, keyOf)));
        Assert.Empty(draw.SeparationConflicts);
    }

    [Fact]
    public void Groups_SeededParticipantsOfOneClubSnakedTogether_StayAndAreReported()
    {
        // Snake over two groups: A = p1, p4; B = p2, p3. Every participant is seeded, so nobody moves.
        var draw = Generate(Groups(Keyed(["A", "B", "B", "A"], i => i), RandomizationPolicy.Seeded(4), groupCount: 2, qualifiers: 1));
        var groupStage = GroupStage(draw);

        Assert.Equal(["p1", "p4"], groupStage.Groups[0].ParticipantIds);
        Assert.Equal(["p2", "p3"], groupStage.Groups[1].ParticipantIds);
        Assert.Equal(
            [("A", "p1,p4", SeparationConflictKind.SameGroup, "A"), ("B", "p2,p3", SeparationConflictKind.SameGroup, "B")],
            draw.SeparationConflicts.Select(conflict => (conflict.Key, string.Join(',', conflict.ParticipantIds), conflict.Kind, conflict.LocationId)));
    }

    [Fact]
    public void Groups_KnockoutIsBuiltFromGroupPositions_SoKeysNeverChangeIt()
    {
        var keyed = Generate(Groups(Keyed(Enumerable.Repeat<string?>("A", 16).ToList()), RandomizationPolicy.Random(8), groupCount: 4));
        var keyless = Generate(Groups(Participants(16), RandomizationPolicy.Random(8), groupCount: 4));

        Assert.Equal(Knockout(keyless).Matches, Knockout(keyed).Matches);
        Assert.All(keyed.SeparationConflicts, conflict => Assert.Equal(SeparationConflictKind.SameGroup, conflict.Kind));
        Assert.Equal(4, keyed.SeparationConflicts.Count);
    }

    // Round robin

    [Theory]
    [InlineData(RandomizationMode.Random)]
    [InlineData(RandomizationMode.Seeded)]
    public void RoundRobin_KeysChangeNothing_AndReportNoConflicts(RandomizationMode mode)
    {
        var keyed = Generate(RoundRobin(Keyed(Enumerable.Repeat<string?>("A", 7).ToList()), new RandomizationPolicy(mode, 21)));
        var keyless = Generate(RoundRobin(Participants(7), new RandomizationPolicy(mode, 21)));

        Assert.Equal(Snapshot(keyless), Snapshot(keyed));
        Assert.Empty(keyed.SeparationConflicts);
    }

    // No keys: the pre-separation behaviour is unchanged

    [Theory]
    [MemberData(nameof(FormatsAndModes))]
    public void NoKeys_BlankOrUniqueKeys_ProduceTheSameDrawAsNullKeys(DrawFormat format, RandomizationMode mode)
    {
        var count = 11;
        var nullKeys = Generate(Request(format, Participants(count, i => i <= 2 ? i : null), mode, 99));
        var blankKeys = Generate(Request(format, Keyed(Enumerable.Range(0, count).Select(i => (string?)(i % 2 == 0 ? "" : "  ")).ToList(), i => i <= 2 ? i : null), mode, 99));
        var uniqueKeys = Generate(Request(format, Keyed(Enumerable.Range(1, count).Select(i => (string?)$"club-{i}").ToList(), i => i <= 2 ? i : null), mode, 99));

        Assert.Equal(Snapshot(nullKeys), Snapshot(blankKeys));
        Assert.Equal(Snapshot(nullKeys), Snapshot(uniqueKeys));
        Assert.Empty(nullKeys.SeparationConflicts);
        Assert.Empty(blankKeys.SeparationConflicts);
        Assert.Empty(uniqueKeys.SeparationConflicts);
    }

    /// <summary>Placements captured from the engine before separation keys existed (commit a913f80).</summary>
    [Theory]
    [InlineData(DrawFormat.SingleElimination, RandomizationMode.Random, "KO[p6-bye;p3-p9;p11-bye;p10-bye;p1-bye;p4-p2;p8-bye;p7-p5]")]
    [InlineData(DrawFormat.SingleElimination, RandomizationMode.Seeded, "KO[p1-bye;p3-p7;p9-bye;p10-bye;p2-bye;p11-p8;p5-bye;p6-p4]")]
    [InlineData(DrawFormat.RoundRobin, RandomizationMode.Random, "RR[p4-p5;p1-p3/p2-p3;p4-p1/p5-p1;p2-p4/p3-p4;p5-p2/p1-p2;p3-p5]")]
    [InlineData(DrawFormat.RoundRobin, RandomizationMode.Seeded, "RR[p1-p5;p2-p3/p4-p3;p1-p2/p5-p2;p4-p1/p3-p1;p5-p4/p2-p4;p3-p5]")]
    [InlineData(DrawFormat.GroupsThenKnockout, RandomizationMode.Random, "G[A=p6,p7,p4,p5;B=p1,p10,p3,p2;C=p8,p11,p9] KO[A1-bye;B2-C2;B1-bye;C1-A2]")]
    [InlineData(DrawFormat.GroupsThenKnockout, RandomizationMode.Seeded, "G[A=p1,p6,p11,p4;B=p2,p10,p3,p8;C=p5,p9,p7] KO[A1-bye;B2-C2;B1-bye;C1-A2]")]
    public void NoKeys_PlacementIsIdenticalToThePreSeparationEngine(DrawFormat format, RandomizationMode mode, string expected)
    {
        var count = format == DrawFormat.RoundRobin ? 5 : 11;

        var draw = Generate(Request(format, Participants(count, i => i <= 2 ? i : null), mode, 2026));

        Assert.Equal(expected, Fingerprint(draw));
    }
}
