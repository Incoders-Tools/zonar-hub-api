using ZonarHub.Domain.Common;
using ZonarHub.Domain.Draws;
using static ZonarHub.Tests.Domain.Draws.DrawTestData;

namespace ZonarHub.Tests.Domain.Draws;

public class DrawRequestValidationTests
{
    private static void AssertFails(DrawRequest request, string expectedCode)
    {
        var result = DrawGenerator.Generate(request);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal($"draws.errors.{expectedCode["draws.".Length..]}", result.Error.MessageKey);
    }

    [Fact]
    public void Generate_NullRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DrawGenerator.Generate(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Generate_FewerThanTwoParticipants_Fails(int count)
    {
        AssertFails(SingleElimination(Participants(count)), "draws.participants_min");
    }

    [Fact]
    public void Generate_NullParticipants_FailsAsTooFew()
    {
        AssertFails(SingleElimination(null!), "draws.participants_min");
    }

    [Fact]
    public void Generate_MoreThan256Participants_Fails()
    {
        AssertFails(SingleElimination(Participants(257)), "draws.participants_max");
    }

    [Fact]
    public void Generate_ExactlyTheLimits_Succeeds()
    {
        Assert.Equal(DrawLimits.MinParticipants, Generate(SingleElimination(Participants(2))).Participants.Count);
        Assert.Equal(DrawLimits.MaxParticipants, Generate(SingleElimination(Participants(256))).Participants.Count);
    }

    [Fact]
    public void Generate_NullParticipantEntry_Fails()
    {
        AssertFails(SingleElimination([new DrawParticipant("a", "A"), null!]), "draws.participant_required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Generate_BlankParticipantId_Fails(string? id)
    {
        AssertFails(SingleElimination([new DrawParticipant("a", "A"), new DrawParticipant(id!, "B")]), "draws.participant_id_required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Generate_BlankDisplayName_Fails(string? name)
    {
        AssertFails(SingleElimination([new DrawParticipant("a", "A"), new DrawParticipant("b", name!)]), "draws.participant_name_required");
    }

    [Fact]
    public void Generate_DuplicateParticipantId_Fails()
    {
        AssertFails(
            SingleElimination([new DrawParticipant("a", "A"), new DrawParticipant("b", "B"), new DrawParticipant("a", "Other")]),
            "draws.duplicate_participant");
    }

    [Fact]
    public void Generate_ParticipantIdsDifferingOnlyByCase_AreDistinct()
    {
        var draw = Generate(SingleElimination([new DrawParticipant("a", "A"), new DrawParticipant("A", "Other")]));

        Assert.Equal(2, draw.Participants.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Generate_NonPositiveSeedRank_Fails(int rank)
    {
        AssertFails(SingleElimination(Participants(4, i => i == 2 ? rank : null)), "draws.invalid_seed_rank");
    }

    [Fact]
    public void Generate_DuplicateSeedRank_Fails()
    {
        AssertFails(SingleElimination(Participants(4, i => i <= 2 ? 1 : null)), "draws.duplicate_seed_rank");
    }

    [Fact]
    public void Generate_MissingSeed_Fails()
    {
        AssertFails(SingleElimination(Participants(4), new RandomizationPolicy(RandomizationMode.Random, null)), "draws.seed_required");
    }

    [Fact]
    public void Generate_MissingRandomizationPolicy_FailsAsMissingSeed()
    {
        var request = SingleElimination(Participants(4)) with { Randomization = null! };

        AssertFails(request, "draws.seed_required");
    }

    [Fact]
    public void Generate_UnknownRandomizationMode_Fails()
    {
        AssertFails(SingleElimination(Participants(4), new RandomizationPolicy((RandomizationMode)42, 1)), "draws.invalid_randomization_mode");
    }

    [Fact]
    public void Generate_UnknownParticipationFormat_Fails()
    {
        AssertFails(SingleElimination(Participants(4), participationFormat: (ParticipationFormat)42), "draws.invalid_participation_format");
    }

    [Fact]
    public void Generate_UnknownFormat_Fails()
    {
        AssertFails(SingleElimination(Participants(4)) with { Format = (DrawFormat)42 }, "draws.format_not_supported");
    }

    [Fact]
    public void Generate_UnknownFormat_FailsEvenWithEveryFormatOption()
    {
        var options = new DrawOptions(RoundRobin: new RoundRobinOptions(), Groups: new GroupStageOptions(GroupCount: 2));

        Assert.Equal(1, options.RoundRobin!.Legs);
        Assert.Equal(2, options.Groups!.QualifiersPerGroup);
        Assert.Null(options.Groups.GroupSize);
        Assert.Null(options.Knockout);
        AssertFails(SingleElimination(Participants(8)) with { Format = (DrawFormat)42, Options = options }, "draws.format_not_supported");
    }

    // Groups then knockout options (D2, D7)

    [Fact]
    public void Generate_GroupCountAndGroupSizeTogether_Fail()
    {
        AssertFails(Groups(Participants(8), groupCount: 2, groupSize: 4), "draws.group_options_conflict");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Generate_NonPositiveGroupCount_Fails(int groupCount)
    {
        AssertFails(Groups(Participants(8), groupCount: groupCount), "draws.invalid_group_count");
    }

    [Theory]
    [InlineData(8, 5)]
    [InlineData(5, 3)]
    [InlineData(2, 2)]
    public void Generate_GroupCountLeavingAGroupBelowTwo_Fails(int count, int groupCount)
    {
        AssertFails(Groups(Participants(count), groupCount: groupCount, qualifiers: 1), "draws.group_too_small");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-4)]
    public void Generate_GroupSizeBelowTwo_Fails(int groupSize)
    {
        AssertFails(Groups(Participants(8), groupSize: groupSize), "draws.invalid_group_size");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(4)]
    public void Generate_QualifiersBelowOneOrNotBelowTheSmallestGroup_Fail(int qualifiers)
    {
        // 10 participants in 3 groups: 4, 3 and 3.
        AssertFails(Groups(Participants(10), groupCount: 3, qualifiers: qualifiers), "draws.invalid_qualifiers");
    }

    [Fact]
    public void Generate_QualifiersBelowTheSmallestGroup_Succeed()
    {
        Assert.True(DrawGenerator.Generate(Groups(Participants(10), groupCount: 3, qualifiers: 2)).IsSuccess);
    }

    [Fact]
    public void Generate_SingleGroupWithOneQualifier_FailsForLackOfAKnockout()
    {
        AssertFails(Groups(Participants(4), groupCount: 1, qualifiers: 1), "draws.knockout_min_qualifiers");
    }

    [Fact]
    public void Generate_GroupsThirdPlaceWithFewerThanFourQualifiers_Fails()
    {
        AssertFails(Groups(Participants(6), groupCount: 3, qualifiers: 1, thirdPlaceMatch: true), "draws.third_place_min_participants");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Generate_GroupsWithRoundRobinLegsOtherThanOneOrTwo_Fail(int legs)
    {
        AssertFails(Groups(Participants(8), groupCount: 2, legs: legs), "draws.invalid_round_robin_legs");
    }

    [Fact]
    public void Generate_GroupsBaseValidation_RunsBeforeGroupOptions()
    {
        AssertFails(Groups(Participants(1), groupCount: 2, groupSize: 4), "draws.participants_min");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Generate_RoundRobinLegsOtherThanOneOrTwo_Fail(int legs)
    {
        AssertFails(RoundRobin(Participants(4), legs: legs), "draws.invalid_round_robin_legs");
    }

    [Fact]
    public void Generate_RoundRobinBaseValidation_RunsBeforeLegs()
    {
        AssertFails(RoundRobin(Participants(1), legs: 3), "draws.participants_min");
    }

    [Fact]
    public void Generate_RoundRobinWithoutOptions_PlaysOneLeg()
    {
        var withoutOptions = Generate(RoundRobin(Participants(4)) with { Options = null });
        var withoutRoundRobinOptions = Generate(RoundRobin(Participants(4)) with { Options = new DrawOptions() });

        Assert.Equal(3, RoundRobinPhase(withoutOptions).Rounds.Count);
        Assert.Equal(3, RoundRobinPhase(withoutRoundRobinOptions).Rounds.Count);
    }

    [Fact]
    public void Generate_SingleElimination_IgnoresInvalidRoundRobinOptions()
    {
        var draw = Generate(SingleElimination(Participants(4)) with { Options = new DrawOptions(RoundRobin: new RoundRobinOptions(9)) });

        Assert.Equal(2, Knockout(draw).Rounds.Count);
    }

    [Fact]
    public void Generate_ThirdPlaceWithFewerThanFourParticipants_Fails()
    {
        AssertFails(SingleElimination(Participants(3), thirdPlaceMatch: true), "draws.third_place_min_participants");
    }

    [Fact]
    public void Generate_NullOptions_UsesDefaults()
    {
        var draw = Generate(SingleElimination(Participants(4)) with { Options = null });

        Assert.Single(Knockout(draw).Rounds[^1].Matches);
    }

    [Fact]
    public void Generate_NullKnockoutOptions_UsesDefaults()
    {
        var draw = Generate(SingleElimination(Participants(4)) with { Options = new DrawOptions() });

        Assert.Single(Knockout(draw).Rounds[^1].Matches);
    }
}
