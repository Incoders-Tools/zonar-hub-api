using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;

namespace ZonarHub.Tests.Application.TournamentDraws;

/// <summary>
/// One camelCase spelling per draw enum value, shared by the database check constraints, the stored jsonb and the API
/// contracts (L7). Parsing is exact (ordinal, case-sensitive) and never falls back to a default.
/// </summary>
public class DrawWireNamesTests
{
    [Fact]
    public void DrawFormat_HasOneNamePerValue_AndParsesBack()
    {
        AssertMap(
            [
                (DrawFormat.SingleElimination, "singleElimination"),
                (DrawFormat.RoundRobin, "roundRobin"),
                (DrawFormat.GroupsThenKnockout, "groupsThenKnockout"),
            ],
            DrawWireNames.ToWireName,
            (string? name, out DrawFormat value) => DrawWireNames.TryParse(name, out value));
    }

    [Fact]
    public void ParticipationFormat_HasOneNamePerValue_AndParsesBack()
    {
        AssertMap(
            [
                (ParticipationFormat.Individual, "individual"),
                (ParticipationFormat.Pair, "pair"),
                (ParticipationFormat.Team, "team"),
            ],
            DrawWireNames.ToWireName,
            (string? name, out ParticipationFormat value) => DrawWireNames.TryParse(name, out value));
    }

    [Fact]
    public void RandomizationMode_HasOneNamePerValue_AndParsesBack()
    {
        AssertMap(
            [(RandomizationMode.Random, "random"), (RandomizationMode.Seeded, "seeded")],
            DrawWireNames.ToWireName,
            (string? name, out RandomizationMode value) => DrawWireNames.TryParse(name, out value));
    }

    [Fact]
    public void PhaseKind_HasOneNamePerValue_AndParsesBack()
    {
        AssertMap(
            [(PhaseKind.GroupStage, "groupStage"), (PhaseKind.Knockout, "knockout"), (PhaseKind.RoundRobin, "roundRobin")],
            DrawWireNames.ToWireName,
            (string? name, out PhaseKind value) => DrawWireNames.TryParse(name, out value));
    }

    [Fact]
    public void SeparationConflictKind_HasOneNamePerValue_AndParsesBack()
    {
        AssertMap(
            [(SeparationConflictKind.FirstRoundMatch, "firstRoundMatch"), (SeparationConflictKind.SameGroup, "sameGroup")],
            DrawWireNames.ToWireName,
            (string? name, out SeparationConflictKind value) => DrawWireNames.TryParse(name, out value));
    }

    [Fact]
    public void MatchStatus_HasOneNamePerValue_AndParsesBack()
    {
        AssertMap(
            [
                (MatchStatus.Scheduled, "scheduled"),
                (MatchStatus.InProgress, "inProgress"),
                (MatchStatus.Completed, "completed"),
                (MatchStatus.Walkover, "walkover"),
                (MatchStatus.Retired, "retired"),
            ],
            DrawWireNames.ToWireName,
            (string? name, out MatchStatus value) => DrawWireNames.TryParse(name, out value));
    }

    [Fact]
    public void MatchSide_HasOneNamePerValue_AndParsesBack()
    {
        AssertMap(
            [(MatchSide.Home, "home"), (MatchSide.Away, "away")],
            DrawWireNames.ToWireName,
            (string? name, out MatchSide value) => DrawWireNames.TryParse(name, out value));
    }

    [Fact]
    public void TournamentDrawStatus_HasOneNamePerValue_AndParsesBack()
    {
        AssertMap(
            [(TournamentDrawStatus.Draft, "draft"), (TournamentDrawStatus.Published, "published")],
            DrawWireNames.ToWireName,
            (string? name, out TournamentDrawStatus value) => DrawWireNames.TryParse(name, out value));
    }

    [Fact]
    public void SlotKinds_AreTheD7Spellings()
    {
        Assert.Equal(
            ["participant", "bye", "winnerOf", "loserOf", "groupPosition"],
            [
                DrawWireNames.SlotKinds.Participant,
                DrawWireNames.SlotKinds.Bye,
                DrawWireNames.SlotKinds.WinnerOf,
                DrawWireNames.SlotKinds.LoserOf,
                DrawWireNames.SlotKinds.GroupPosition,
            ]);
    }

    [Fact]
    public void UndefinedEnumValue_HasNoName()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => ((MatchStatus)99).ToWireName());

        Assert.Equal("value", error.ParamName);
    }

    private delegate bool TryParser<TEnum>(string? name, out TEnum value);

    private static void AssertMap<TEnum>(
        IReadOnlyList<(TEnum Value, string Name)> expected,
        Func<TEnum, string> toName,
        TryParser<TEnum> tryParse)
        where TEnum : struct, Enum
    {
        // Every defined value is mapped: a new enum member must get a spelling before it can be stored.
        Assert.Equal(Enum.GetValues<TEnum>().Order(), expected.Select(pair => pair.Value).Order());

        foreach (var (value, name) in expected)
        {
            Assert.Equal(name, toName(value));
            Assert.True(tryParse(name, out var parsed));
            Assert.Equal(value, parsed);
        }

        // Exact spelling only: other casing, the C# member name, blanks and null are rejected with the default value.
        var rejected = expected
            .SelectMany(pair => new[] { pair.Name.ToUpperInvariant(), $" {pair.Name}" })
            .Concat(["", "unknown", null]);
        foreach (var name in rejected)
        {
            Assert.False(tryParse(name, out var parsed), name);
            Assert.Equal(default, parsed);
        }
    }
}
