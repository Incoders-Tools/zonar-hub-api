using ZonarHub.Domain.Common;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;
using static ZonarHub.Tests.Domain.Draws.DrawTestData;
using static ZonarHub.Tests.Domain.Draws.Results.ResultTestData;

namespace ZonarHub.Tests.Domain.Draws.Results;

public class DrawProgressionTests
{
    private static Draw Knockout8() => Generate(SingleElimination(Participants(8, i => i), thirdPlaceMatch: true));

    /// <summary>One group of the given participants (seed rank = index), whose top two meet in a final.</summary>
    private static Draw SingleGroup(int count, Func<int, int?>? seedRank = null)
    {
        var draw = Generate(Groups(Participants(count, seedRank ?? (i => i)), groupCount: 1, qualifiers: 2));
        Assert.Single(GroupStage(draw).Groups);
        return draw;
    }

    private static IReadOnlyList<string> Order(ResolvedDraw resolved, string phaseId, string? groupId = null) =>
        resolved.FindStandings(phaseId, groupId)!.Rows.Select(row => row.ParticipantId).ToList();

    /// <summary>
    /// Hand-built knockout: X (p1 vs p2) feeds B, a bye match, whose winner meets p3 in F; Y is a plain bye for p4.
    /// </summary>
    private static Draw ByeChain()
    {
        var participants = Participants(4);
        var rounds = new[]
        {
            new Round(1, "quarterfinal", [
                new Match("KO-R1-M1", 1, 1, new ParticipantSlot("p1"), new ParticipantSlot("p2"), "KO-R2-M1"),
                new Match("KO-R1-M2", 1, 2, new ParticipantSlot("p4"), Slot.Bye, "KO-R2-M2"),
            ]),
            new Round(2, "semifinal", [
                new Match("KO-R2-M1", 2, 1, new WinnerOfSlot("KO-R1-M1"), Slot.Bye, "KO-R3-M1"),
                new Match("KO-R2-M2", 2, 2, new WinnerOfSlot("KO-R1-M2"), new ParticipantSlot("p3"), "KO-R3-M1"),
            ]),
            new Round(3, "final", [
                new Match("KO-R3-M1", 3, 1, new WinnerOfSlot("KO-R2-M1"), new WinnerOfSlot("KO-R2-M2")),
            ]),
        };

        return new Draw(
            DrawFormat.SingleElimination,
            ParticipationFormat.Individual,
            RandomizationMode.Seeded,
            1,
            participants,
            [new Phase("KO", PhaseKind.Knockout, rounds)]);
    }

    // Knockout progression

    [Fact]
    public void Resolve_Knockout_ResultsFlowToTheFinalAndTheThirdPlaceMatch()
    {
        var draw = Knockout8();
        var results = new List<MatchResult>();

        foreach (var round in Knockout(draw).Rounds)
            results.AddRange(round.Matches.Select(match => Win(match.Id, MatchSide.Home)));

        var resolved = Resolve(draw, results);

        Assert.Equal(("p1", "p4"), Sides(Match(resolved, "KO-R2-M1")));
        Assert.Equal(("p2", "p3"), Sides(Match(resolved, "KO-R2-M2")));
        Assert.Equal(("p1", "p2"), Sides(Match(resolved, "KO-R3-M1")));
        Assert.Equal(("p4", "p3"), Sides(Match(resolved, "KO-R3-M2")));
        Assert.Equal("p1", Match(resolved, "KO-R3-M1").WinnerParticipantId);
        Assert.Equal("p2", Match(resolved, "KO-R3-M1").LoserParticipantId);
        Assert.Equal("p4", Match(resolved, "KO-R3-M2").WinnerParticipantId);
        Assert.Same(results[^1], Match(resolved, "KO-R3-M2").Result);
        Assert.Equal(draw.Phases.SelectMany(phase => phase.Matches).Select(match => match.Id), resolved.Matches.Select(match => match.Id));
        Assert.Same(draw, resolved.Draw);
    }

    [Fact]
    public void Resolve_Knockout_AwayWinnersAdvanceToo()
    {
        var draw = Knockout8();
        var results = Knockout(draw).Rounds[0].Matches.Select(match => Win(match.Id, MatchSide.Away)).ToList();

        var resolved = Resolve(draw, results);

        Assert.Equal(("p8", "p5"), Sides(Match(resolved, "KO-R2-M1")));
        Assert.Equal(("p7", "p6"), Sides(Match(resolved, "KO-R2-M2")));
    }

    [Fact]
    public void Resolve_Knockout_UnresolvedSlotsStayNull()
    {
        var draw = Knockout8();
        var resolved = Resolve(draw, [Win("KO-R1-M1", MatchSide.Home), new MatchResult("KO-R1-M2", MatchStatus.InProgress, [S(3, 2)])]);

        Assert.Equal(("p1", "p8"), Sides(Match(resolved, "KO-R1-M1")));
        Assert.Equal(("p1", null), Sides(Match(resolved, "KO-R2-M1")));
        Assert.Equal((null, null), Sides(Match(resolved, "KO-R2-M2")));
        Assert.Equal((null, null), Sides(Match(resolved, "KO-R3-M1")));
        Assert.Equal((null, null), Sides(Match(resolved, "KO-R3-M2")));
        Assert.Null(Match(resolved, "KO-R1-M2").WinnerParticipantId);
        Assert.Null(Match(resolved, "KO-R1-M2").LoserParticipantId);
        Assert.Null(Match(resolved, "KO-R2-M2").Result);
        Assert.Null(resolved.FindMatch("KO-R9-M9"));
    }

    [Fact]
    public void Resolve_Knockout_WalkoverAndRetiredDecideTheMatch()
    {
        var draw = Knockout8();
        var resolved = Resolve(draw, [
            new MatchResult("KO-R1-M1", MatchStatus.Walkover, winnerSide: MatchSide.Away),
            new MatchResult("KO-R1-M2", MatchStatus.Retired, [S(6, 3), S(2, 1)], MatchSide.Away),
        ]);

        Assert.Equal(("p8", "p5"), Sides(Match(resolved, "KO-R2-M1")));
    }

    [Fact]
    public void Resolve_Knockout_ByeRecipientsAdvanceWithoutAResult()
    {
        var draw = Generate(SingleElimination(Participants(6, i => i)));
        var resolved = Resolve(draw, []);

        var byes = resolved.Matches.Where(match => match.IsBye).ToList();
        Assert.Equal(2, byes.Count);
        Assert.All(byes, bye => Assert.NotNull(bye.WinnerParticipantId));
        Assert.All(byes, bye => Assert.Null(bye.LoserParticipantId));
        Assert.Equal(new[] { "p1", "p2" }, byes.Select(bye => bye.WinnerParticipantId));
        Assert.Equal("p1", Match(resolved, "KO-R2-M1").HomeParticipantId);
        Assert.Equal("p2", Match(resolved, "KO-R2-M2").HomeParticipantId);
    }

    [Fact]
    public void Resolve_Knockout_ByesChainThroughWinnerOfSlots()
    {
        var draw = ByeChain();

        var pending = Resolve(draw, []);
        Assert.Equal((null, null), Sides(Match(pending, "KO-R2-M1")));
        Assert.Null(Match(pending, "KO-R2-M1").WinnerParticipantId);
        Assert.Equal(("p4", "p3"), Sides(Match(pending, "KO-R2-M2")));
        Assert.Equal((null, null), Sides(Match(pending, "KO-R3-M1")));

        var resolved = Resolve(draw, [Win("KO-R1-M1", MatchSide.Away), Win("KO-R2-M2", MatchSide.Away)]);
        Assert.Equal("p2", Match(resolved, "KO-R2-M1").WinnerParticipantId);
        Assert.Equal(("p2", "p3"), Sides(Match(resolved, "KO-R3-M1")));
    }

    [Fact]
    public void Resolve_Knockout_SlotsFromAMissingMatchStayNull()
    {
        var draw = ByeChain() with
        {
            Phases = [new Phase("KO", PhaseKind.Knockout, [new Round(1, "final", [new Match("KO-R1-M1", 1, 1, new WinnerOfSlot("KO-R0-M1"), new LoserOfSlot("KO-R0-M1"))])])],
        };

        Assert.Equal((null, null), Sides(Match(Resolve(draw, []), "KO-R1-M1")));
    }

    // Group standings

    [Fact]
    public void Resolve_GroupStandings_OrderByMatchesWon()
    {
        var draw = SingleGroup(4);
        Assert.Equal(new[] { "p1", "p2", "p3", "p4" }, GroupStage(draw).Groups[0].ParticipantIds);

        var resolved = Resolve(draw, [
            Beat(draw, "p4", "p1"), Beat(draw, "p4", "p2"), Beat(draw, "p4", "p3"),
            Beat(draw, "p3", "p1"), Beat(draw, "p3", "p2"), Beat(draw, "p2", "p1"),
        ]);

        var standings = resolved.FindStandings("G", "A")!;
        Assert.True(standings.IsComplete);
        Assert.Equal("G", standings.PhaseId);
        Assert.Equal("A", standings.GroupId);
        Assert.Equal(new[] { "p4", "p3", "p2", "p1" }, standings.Rows.Select(row => row.ParticipantId));
        Assert.Equal(new[] { 1, 2, 3, 4 }, standings.Rows.Select(row => row.Position));

        var leader = standings.Rows[0];
        Assert.Equal(new StandingRow("p4", 1, 3, 3, 0, 6, 0, 36, 21), leader);
        Assert.Equal(6, leader.SetDifference);
        Assert.Equal(15, leader.GameDifference);
        Assert.Equal(new StandingRow("p1", 4, 3, 0, 3, 0, 6, 21, 36), standings.Rows[3]);
        Assert.Equal(3, standings.Rows[3].Lost);
    }

    [Fact]
    public void Resolve_GroupStandings_BreakTiesBySetDifference()
    {
        var draw = SingleGroup(3);
        var resolved = Resolve(draw, [
            Beat(draw, "p1", "p2", (6, 4), (3, 6), (10, 8)),
            Beat(draw, "p2", "p3", (6, 4), (3, 6), (10, 8)),
            Beat(draw, "p3", "p1", (6, 4), (6, 4)),
        ]);

        Assert.Equal(new[] { "p3", "p2", "p1" }, Order(resolved, "G", "A"));
    }

    [Fact]
    public void Resolve_GroupStandings_BreakTiesByGameDifference()
    {
        var draw = SingleGroup(3);
        var resolved = Resolve(draw, [
            Beat(draw, "p1", "p2", (6, 0), (6, 0)),
            Beat(draw, "p2", "p3", (6, 4), (6, 4)),
            Beat(draw, "p3", "p1", (6, 3), (6, 3)),
        ]);

        Assert.Equal(new[] { "p1", "p3", "p2" }, Order(resolved, "G", "A"));
    }

    [Fact]
    public void Resolve_GroupStandings_BreakATwoWayTieByHeadToHead()
    {
        var draw = SingleGroup(4);
        var resolved = Resolve(draw, [
            Beat(draw, "p3", "p2"), Beat(draw, "p2", "p4"), Beat(draw, "p2", "p1"),
            Beat(draw, "p3", "p4"), Beat(draw, "p1", "p3"), Beat(draw, "p4", "p1"),
        ]);

        Assert.Equal(new[] { "p3", "p2", "p4", "p1" }, Order(resolved, "G", "A"));
    }

    [Fact]
    public void Resolve_GroupStandings_KeepTheGroupOrderForAThreeWayTie()
    {
        var draw = SingleGroup(3, i => 4 - i);
        Assert.Equal(new[] { "p3", "p2", "p1" }, GroupStage(draw).Groups[0].ParticipantIds);

        var resolved = Resolve(draw, [Beat(draw, "p1", "p2"), Beat(draw, "p2", "p3"), Beat(draw, "p3", "p1")]);

        Assert.Equal(new[] { "p3", "p2", "p1" }, Order(resolved, "G", "A"));
    }

    [Fact]
    public void Resolve_GroupStandings_KeepTheGroupOrderWhenATwoWayHeadToHeadIsLevel()
    {
        var draw = Generate(Groups(Participants(4, i => 5 - i), groupCount: 2, qualifiers: 1, legs: 2));
        var group = GroupStage(draw).Groups[0];
        Assert.Equal(new[] { "p4", "p1" }, group.ParticipantIds);

        // Mirrored legs won at home: each participant wins one leg with the same score.
        var resolved = Resolve(draw, group.Matches.Select(match => Win(match.Id, MatchSide.Home)).ToList());

        Assert.Equal(new[] { "p4", "p1" }, Order(resolved, "G", "A"));
        Assert.All(resolved.FindStandings("G", "A")!.Rows, row => Assert.Equal(1, row.Won));
    }

    [Fact]
    public void Resolve_GroupStandings_CountWalkoverAndRetiredMatches()
    {
        var draw = Generate(Groups(Participants(4, i => i), groupCount: 2, qualifiers: 1));
        Assert.Equal(new[] { "p1", "p4" }, GroupStage(draw).Groups[0].ParticipantIds);
        Assert.Equal(new[] { "p2", "p3" }, GroupStage(draw).Groups[1].ParticipantIds);

        var walkover = FindMatch(draw, "p1", "p4");
        var retired = FindMatch(draw, "p2", "p3");
        var p2Side = SideOf(retired, "p2");
        var retiredSets = p2Side == MatchSide.Home ? new[] { S(6, 3), S(2, 1) } : new[] { S(3, 6), S(1, 2) };

        var resolved = Resolve(draw, [
            new MatchResult(walkover.Id, MatchStatus.Walkover, winnerSide: SideOf(walkover, "p4")),
            new MatchResult(retired.Id, MatchStatus.Retired, retiredSets, SideOf(retired, "p3")),
        ]);

        var groupA = resolved.FindStandings("G", "A")!;
        Assert.Equal(new StandingRow("p4", 1, 1, 1, 0, 2, 0, 12, 0), groupA.Rows[0]);
        Assert.Equal(new StandingRow("p1", 2, 1, 0, 1, 0, 2, 0, 12), groupA.Rows[1]);

        var groupB = resolved.FindStandings("G", "B")!;
        Assert.Equal(new StandingRow("p3", 1, 1, 1, 0, 0, 1, 4, 8), groupB.Rows[0]);
        Assert.Equal(new StandingRow("p2", 2, 1, 0, 1, 1, 0, 8, 4), groupB.Rows[1]);

        Assert.Equal(("p4", "p3"), Sides(Match(resolved, "KO-R1-M1")));

        // The same walkover won by the other side.
        var reversed = Resolve(draw, [new MatchResult(walkover.Id, MatchStatus.Walkover, winnerSide: SideOf(walkover, "p1"))]);
        Assert.Equal(new StandingRow("p1", 1, 1, 1, 0, 2, 0, 12, 0), reversed.FindStandings("G", "A")!.Rows[0]);
    }

    [Fact]
    public void Resolve_GroupStandings_CountASuperTiebreakAsOneGame()
    {
        var draw = Generate(Groups(Participants(4, i => i), groupCount: 2, qualifiers: 1));

        var resolved = Resolve(draw, [Beat(draw, "p1", "p4", (6, 4), (3, 6), (10, 8))]);

        var winner = resolved.FindStandings("G", "A")!.Rows[0];
        Assert.Equal(new StandingRow("p1", 1, 1, 1, 0, 2, 1, 10, 10), winner);
    }

    [Fact]
    public void Resolve_GroupStandings_IgnoreUndecidedMatchesAndListEveryone()
    {
        var draw = SingleGroup(4);
        var match = FindMatch(draw, "p1", "p2");
        var resolved = Resolve(draw, [new MatchResult(match.Id, MatchStatus.InProgress, [S(6, 4), S(2, 2)])]);

        var standings = resolved.FindStandings("G", "A")!;
        Assert.False(standings.IsComplete);
        Assert.Equal(new[] { "p1", "p2", "p3", "p4" }, standings.Rows.Select(row => row.ParticipantId));
        Assert.All(standings.Rows, row => Assert.Equal(0, row.Played));
    }

    [Fact]
    public void Resolve_GroupPosition_ResolvesOnlyWhenTheGroupIsComplete()
    {
        var draw = SingleGroup(4);
        var results = new List<MatchResult>
        {
            Beat(draw, "p1", "p2"), Beat(draw, "p1", "p3"), Beat(draw, "p1", "p4"),
            Beat(draw, "p2", "p3"), Beat(draw, "p2", "p4"),
        };

        var partial = Resolve(draw, results);
        Assert.False(partial.FindStandings("G", "A")!.IsComplete);
        Assert.Equal(new[] { 1, 2, 3, 4 }, partial.FindStandings("G", "A")!.Rows.Select(row => row.Position));
        Assert.Equal((null, null), Sides(Match(partial, "KO-R1-M1")));

        results.Add(Beat(draw, "p4", "p3"));
        var complete = Resolve(draw, results);
        Assert.True(complete.FindStandings("G", "A")!.IsComplete);
        Assert.Equal(("p1", "p2"), Sides(Match(complete, "KO-R1-M1")));
        Assert.Equal("A", Match(complete, Beat(draw, "p4", "p3").MatchId).GroupId);
        Assert.Equal("G", Match(complete, Beat(draw, "p4", "p3").MatchId).PhaseId);
        Assert.Null(Match(complete, "KO-R1-M1").GroupId);
    }

    [Fact]
    public void Resolve_GroupPosition_BeyondTheGroupSizeStaysNull()
    {
        var draw = ByeChain() with
        {
            Phases =
            [
                new Phase("G", PhaseKind.GroupStage, [new Round(1, "matchday", [new Match("G-A-R1-M1", 1, 1, new ParticipantSlot("p1"), new ParticipantSlot("p2"))])])
                {
                    Groups = [new Group("A", ["p1", "p2"], [new Round(1, "matchday", [new Match("G-A-R1-M1", 1, 1, new ParticipantSlot("p1"), new ParticipantSlot("p2"))])])],
                },
                new Phase("KO", PhaseKind.Knockout, [new Round(1, "final", [new Match("KO-R1-M1", 1, 1, new GroupPositionSlot("A", 1), new GroupPositionSlot("A", 3))])]),
            ],
        };

        var resolved = Resolve(draw, [Win("G-A-R1-M1", MatchSide.Home)]);

        Assert.Equal(("p1", null), Sides(Match(resolved, "KO-R1-M1")));
    }

    // Round robin standings

    [Fact]
    public void Resolve_RoundRobin_ComputesPhaseStandings()
    {
        var draw = Generate(RoundRobin(Participants(3, i => i)));
        var results = new List<MatchResult> { Beat(draw, "p3", "p1"), Beat(draw, "p3", "p2") };

        var partial = Resolve(draw, results);
        var pending = partial.FindStandings("RR")!;
        Assert.False(pending.IsComplete);
        Assert.Null(pending.GroupId);
        Assert.Equal(new[] { "p3", "p1", "p2" }, pending.Rows.Select(row => row.ParticipantId));

        results.Add(Beat(draw, "p2", "p1"));
        var standings = Resolve(draw, results).FindStandings("RR")!;
        Assert.True(standings.IsComplete);
        Assert.Equal(new[] { "p3", "p2", "p1" }, standings.Rows.Select(row => row.ParticipantId));
        Assert.Single(Resolve(draw, results).Standings);
        Assert.Null(Resolve(draw, results).FindStandings("G", "A"));
    }

    [Fact]
    public void Resolve_RoundRobin_UsesSeedThenEntryOrderAsTheLastTieBreak()
    {
        var participants = new[]
        {
            new DrawParticipant("x", "X"),
            new DrawParticipant("y", "Y", seedRank: 2),
            new DrawParticipant("z", "Z", seedRank: 1),
            new DrawParticipant("w", "W"),
        };
        var draw = Generate(RoundRobin(participants));

        Assert.Equal(new[] { "z", "y", "x", "w" }, Order(Resolve(draw, []), "RR"));

        var random = Generate(RoundRobin(participants, RandomizationPolicy.Random(5)));
        Assert.Equal(new[] { "x", "y", "z", "w" }, Order(Resolve(random, []), "RR"));
    }

    [Fact]
    public void Resolve_Knockout_HasNoStandings()
    {
        Assert.Empty(Resolve(Knockout8(), []).Standings);
    }

    // Stored results that do not fit the draw

    [Fact]
    public void Resolve_RejectsResultsThatDoNotFitTheDraw()
    {
        var draw = Generate(SingleElimination(Participants(6, i => i)));
        var bye = draw.Phases[0].Matches.First(match => match.IsBye);

        AssertFailure("draws.results.unknown_match", ErrorType.NotFound, DrawProgression.Resolve(draw, [Win("KO-R9-M1", MatchSide.Home)], ScoringRules.Default));
        AssertFailure("draws.results.duplicate_result", ErrorType.Conflict, DrawProgression.Resolve(draw, [Win("KO-R1-M2", MatchSide.Home), Win("KO-R1-M2", MatchSide.Away)], ScoringRules.Default));
        AssertFailure("draws.results.bye_match", ErrorType.Conflict, DrawProgression.Resolve(draw, [Win(bye.Id, MatchSide.Home)], ScoringRules.Default));
        AssertFailure("draws.results.invalid_set", ErrorType.Validation, DrawProgression.Resolve(draw, [Win("KO-R1-M2", MatchSide.Home, (8, 6), (6, 0))], ScoringRules.Default));
    }

    [Fact]
    public void Resolve_KeepsAStoredResultWhoseParticipantsArePending()
    {
        var draw = Knockout8();
        var scheduled = new MatchResult("KO-R3-M1", MatchStatus.Scheduled, courtName: "Central");

        var resolved = Resolve(draw, [scheduled, Win("KO-R2-M1", MatchSide.Home)]);

        Assert.Same(scheduled, Match(resolved, "KO-R3-M1").Result);
        Assert.Null(Match(resolved, "KO-R2-M1").WinnerParticipantId);
    }

    [Fact]
    public void Resolve_RequiresArguments()
    {
        Assert.Throws<ArgumentNullException>(() => DrawProgression.Resolve(null!, [], ScoringRules.Default));
        Assert.Throws<ArgumentNullException>(() => DrawProgression.Resolve(Knockout8(), null!, ScoringRules.Default));
        Assert.Throws<ArgumentNullException>(() => DrawProgression.Resolve(Knockout8(), [], null!));
        Assert.Throws<ArgumentException>(() => DrawProgression.Resolve(Knockout8(), [null!], ScoringRules.Default));
    }

    // Edit rules

    [Fact]
    public void CanRecordResult_AcceptsAValidResultBetweenResolvedParticipants()
    {
        var resolved = Resolve(Knockout8(), []);

        Assert.True(DrawProgression.CanRecordResult(resolved, "KO-R1-M1", Win("KO-R1-M1", MatchSide.Home)).IsSuccess);
        Assert.True(DrawProgression.CanRecordResult(resolved, "KO-R1-M1", null).IsSuccess);
    }

    [Fact]
    public void CanRecordResult_RejectsUnknownByeAndPendingMatches()
    {
        var knockout = Resolve(Knockout8(), []);
        var withByes = Resolve(Generate(SingleElimination(Participants(6, i => i))), []);
        var bye = withByes.Matches.First(match => match.IsBye);

        AssertFailure("draws.results.unknown_match", ErrorType.NotFound, DrawProgression.CanRecordResult(knockout, "KO-R9-M9", Win("KO-R9-M9", MatchSide.Home)));
        AssertFailure("draws.results.bye_match", ErrorType.Conflict, DrawProgression.CanRecordResult(withByes, bye.Id, Win(bye.Id, MatchSide.Home)));
        AssertFailure("draws.results.participants_pending", ErrorType.Conflict, DrawProgression.CanRecordResult(knockout, "KO-R2-M1", Win("KO-R2-M1", MatchSide.Home)));
        AssertFailure("draws.results.participants_pending", ErrorType.Conflict, DrawProgression.CanRecordResult(knockout, "KO-R2-M1", new MatchResult("KO-R2-M1", MatchStatus.Scheduled)));
    }

    [Fact]
    public void CanRecordResult_RejectsAnInvalidScore()
    {
        var resolved = Resolve(Knockout8(), []);

        AssertFailure("draws.results.winner_mismatch", ErrorType.Validation, DrawProgression.CanRecordResult(
            resolved, "KO-R1-M1", new MatchResult("KO-R1-M1", MatchStatus.Completed, [S(6, 4), S(6, 4)], MatchSide.Away)));
    }

    [Fact]
    public void CanRecordResult_UsesTheRulesTheDrawWasResolvedWith()
    {
        var resolved = Resolve(Knockout8(), [], new ScoringRules(thirdSetSuperTiebreak: false));
        var result = new MatchResult("KO-R1-M1", MatchStatus.Completed, [S(6, 4), S(4, 6), S(10, 8)], MatchSide.Home);

        AssertFailure("draws.results.invalid_set", ErrorType.Validation, DrawProgression.CanRecordResult(resolved, "KO-R1-M1", result));
        Assert.False(resolved.Rules.ThirdSetSuperTiebreak);
    }

    [Fact]
    public void CanRecordResult_LocksAResultOnceItsWinnerPlayedTheNextMatch()
    {
        var draw = Knockout8();
        var first = Win("KO-R1-M1", MatchSide.Home);
        var results = new List<MatchResult> { first, Win("KO-R1-M2", MatchSide.Home) };

        var beforeNext = Resolve(draw, [.. results, new MatchResult("KO-R2-M1", MatchStatus.Scheduled, courtName: "Central")]);
        Assert.True(DrawProgression.CanRecordResult(beforeNext, "KO-R1-M1", Win("KO-R1-M1", MatchSide.Away)).IsSuccess);
        Assert.True(DrawProgression.CanRecordResult(beforeNext, "KO-R1-M1", null).IsSuccess);

        var afterNext = Resolve(draw, [.. results, new MatchResult("KO-R2-M1", MatchStatus.InProgress, [S(2, 1)])]);
        AssertFailure("draws.results.locked_by_next_match", ErrorType.Conflict, DrawProgression.CanRecordResult(afterNext, "KO-R1-M1", Win("KO-R1-M1", MatchSide.Away)));
        AssertFailure("draws.results.locked_by_next_match", ErrorType.Conflict, DrawProgression.CanRecordResult(afterNext, "KO-R1-M1", Win("KO-R1-M1", MatchSide.Home, (6, 0), (6, 0))));
        AssertFailure("draws.results.locked_by_next_match", ErrorType.Conflict, DrawProgression.CanRecordResult(afterNext, "KO-R1-M1", null));

        // Same status, sets and winner: only details such as the court or times change.
        Assert.True(DrawProgression.CanRecordResult(afterNext, "KO-R1-M1", first with { CourtName = "Court 2", FinishedAtUtc = new DateTime(2026, 11, 7, 20, 0, 0, DateTimeKind.Utc) }).IsSuccess);

        // The next match itself stays editable.
        Assert.True(DrawProgression.CanRecordResult(afterNext, "KO-R2-M1", Win("KO-R2-M1", MatchSide.Away)).IsSuccess);
    }

    [Fact]
    public void CanRecordResult_LocksASemifinalOnceItsLoserPlayedTheThirdPlaceMatch()
    {
        var draw = Knockout8();
        var results = Knockout(draw).Rounds.Take(2).SelectMany(round => round.Matches).Select(match => Win(match.Id, MatchSide.Home)).ToList();
        results.Add(new MatchResult("KO-R3-M2", MatchStatus.Walkover, winnerSide: MatchSide.Home));

        var resolved = Resolve(draw, results);

        AssertFailure("draws.results.locked_by_next_match", ErrorType.Conflict, DrawProgression.CanRecordResult(resolved, "KO-R2-M1", null));
    }

    [Fact]
    public void CanRecordResult_LocksThroughAByeMatch()
    {
        var draw = ByeChain();

        var beforeFinal = Resolve(draw, [Win("KO-R1-M1", MatchSide.Home), Win("KO-R2-M2", MatchSide.Home)]);
        Assert.True(DrawProgression.CanRecordResult(beforeFinal, "KO-R1-M1", null).IsSuccess);

        var afterFinal = Resolve(draw, [Win("KO-R1-M1", MatchSide.Home), Win("KO-R2-M2", MatchSide.Home), Win("KO-R3-M1", MatchSide.Home)]);
        AssertFailure("draws.results.locked_by_next_match", ErrorType.Conflict, DrawProgression.CanRecordResult(afterFinal, "KO-R1-M1", null));
    }

    [Fact]
    public void CanRecordResult_LocksGroupMatchesOnceTheKnockoutTheyFeedHasAResult()
    {
        var draw = Generate(Groups(Participants(4, i => i), groupCount: 2, qualifiers: 1));
        var groupA = FindMatch(draw, "p1", "p4");
        var results = new List<MatchResult> { Beat(draw, "p1", "p4"), Beat(draw, "p2", "p3") };

        var beforeKnockout = Resolve(draw, results);
        Assert.True(DrawProgression.CanRecordResult(beforeKnockout, groupA.Id, Beat(draw, "p4", "p1")).IsSuccess);

        var afterKnockout = Resolve(draw, [.. results, Win("KO-R1-M1", MatchSide.Home)]);
        AssertFailure("draws.results.locked_by_next_match", ErrorType.Conflict, DrawProgression.CanRecordResult(afterKnockout, groupA.Id, Beat(draw, "p4", "p1")));
        AssertFailure("draws.results.locked_by_next_match", ErrorType.Conflict, DrawProgression.CanRecordResult(afterKnockout, groupA.Id, null));
    }

    [Fact]
    public void CanRecordResult_NeverLocksRoundRobinMatches()
    {
        var draw = Generate(RoundRobin(Participants(3, i => i)));
        var results = new List<MatchResult> { Beat(draw, "p1", "p2"), Beat(draw, "p1", "p3"), Beat(draw, "p2", "p3") };
        var resolved = Resolve(draw, results);

        Assert.True(DrawProgression.CanRecordResult(resolved, results[0].MatchId, Beat(draw, "p2", "p1")).IsSuccess);
    }

    [Fact]
    public void CanRecordResult_RequiresArgumentsAndAMatchingId()
    {
        var resolved = Resolve(Knockout8(), []);

        Assert.Throws<ArgumentNullException>(() => DrawProgression.CanRecordResult(null!, "KO-R1-M1", null));
        Assert.Throws<ArgumentNullException>(() => DrawProgression.CanRecordResult(resolved, null!, null));
        Assert.Throws<ArgumentException>(() => DrawProgression.CanRecordResult(resolved, "KO-R1-M1", Win("KO-R1-M2", MatchSide.Home)));
    }

    private static (string?, string?) Sides(ResolvedMatch match) => (match.HomeParticipantId, match.AwayParticipantId);

    private static void AssertFailure(string code, ErrorType type, Result result)
    {
        Assert.True(result.IsFailure, code);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
    }

    private static void AssertFailure<T>(string code, ErrorType type, Result<T> result)
    {
        Assert.True(result.IsFailure, code);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
    }
}
