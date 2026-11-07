using System.Text.Json;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Infrastructure.Persistence.Supabase.Draws;
using static ZonarHub.Tests.Domain.Draws.DrawTestData;

namespace ZonarHub.Tests.Infrastructure.Persistence;

/// <summary>
/// Stored jsonb documents of a draw (D2): the structure snapshot is the source of truth for the bracket shape, so it
/// must round-trip exactly through a stable, versioned (<c>"version": 1</c>) camelCase shape.
/// </summary>
public class TournamentDrawJsonTests
{
    [Fact]
    public void Structure_HandBuiltDraw_HasTheVersionedShape()
    {
        var draw = new Draw(
            DrawFormat.SingleElimination,
            ParticipationFormat.Pair,
            RandomizationMode.Seeded,
            ulong.MaxValue,
            [
                new DrawParticipant("p1", "Ana / Bea", ["Ana", "Bea"], seedRank: 1, separationKey: "Club Norte"),
                new DrawParticipant("p2", "Caro / Dani"),
            ],
            [
                new Phase("KO", PhaseKind.Knockout, [
                    new Round(1, "semifinal", [
                        new Match("KO-R1-M1", 1, 1, new ParticipantSlot("p1"), Slot.Bye, "KO-R2-M1", "KO-R2-M2"),
                        new Match("KO-R1-M2", 1, 2, new GroupPositionSlot("A", 2), new LoserOfSlot("X")),
                    ]),
                    new Round(2, "final", [new Match("KO-R2-M1", 2, 1, new WinnerOfSlot("KO-R1-M1"), new ParticipantSlot("p2"))])
                    {
                        RestingParticipantIds = ["p9"],
                    },
                ]),
            ])
        {
            SeparationConflicts = [new SeparationConflict("Club Norte", ["p1", "p2"], SeparationConflictKind.FirstRoundMatch, "KO-R1-M1")],
        };

        var json = TournamentDrawJson.WriteDraw(draw).GetRawText();

        Assert.Equal(
            """
            {"version":1,"format":"singleElimination","participationFormat":"pair","randomizationMode":"seeded","seed":"18446744073709551615",
            "participants":[{"id":"p1","displayName":"Ana / Bea","members":["Ana","Bea"],"seedRank":1,"separationKey":"Club Norte"},
            {"id":"p2","displayName":"Caro / Dani","members":[],"seedRank":null,"separationKey":null}],
            "phases":[{"id":"KO","kind":"knockout","groups":[],"rounds":[
            {"number":1,"name":"semifinal","restingParticipantIds":[],"matches":[
            {"id":"KO-R1-M1","roundNumber":1,"number":1,"home":{"kind":"participant","participantId":"p1"},"away":{"kind":"bye"},"winnerAdvancesTo":"KO-R2-M1","loserAdvancesTo":"KO-R2-M2"},
            {"id":"KO-R1-M2","roundNumber":1,"number":2,"home":{"kind":"groupPosition","groupId":"A","rank":2},"away":{"kind":"loserOf","matchId":"X"},"winnerAdvancesTo":null,"loserAdvancesTo":null}]},
            {"number":2,"name":"final","restingParticipantIds":["p9"],"matches":[
            {"id":"KO-R2-M1","roundNumber":2,"number":1,"home":{"kind":"winnerOf","matchId":"KO-R1-M1"},"away":{"kind":"participant","participantId":"p2"},"winnerAdvancesTo":null,"loserAdvancesTo":null}]}]}],
            "separationConflicts":[{"key":"Club Norte","participantIds":["p1","p2"],"kind":"firstRoundMatch","locationId":"KO-R1-M1"}]}
            """.ReplaceLineEndings(string.Empty),
            json);
        AssertRoundTrips(draw);
    }

    [Fact]
    public void Structure_SingleEliminationWithByesAndThirdPlace_RoundTrips()
    {
        var draw = Generate(SingleElimination(
            Pairs(6, seedRank: i => i <= 2 ? i : null),
            RandomizationPolicy.Seeded(77),
            thirdPlaceMatch: true,
            participationFormat: ParticipationFormat.Pair));
        var knockout = Knockout(draw);

        Assert.Contains(knockout.Matches, match => match.IsBye);
        Assert.Contains(knockout.Matches, match => match.Home is LoserOfSlot);
        AssertRoundTrips(draw);
    }

    [Fact]
    public void Structure_RoundRobinWithOddCount_RoundTripsTheRests()
    {
        var draw = Generate(RoundRobin(Participants(5), RandomizationPolicy.Random(13), legs: 2));

        Assert.All(RoundRobinPhase(draw).Rounds, round => Assert.Single(round.RestingParticipantIds));
        AssertRoundTrips(draw);
    }

    [Fact]
    public void Structure_GroupsThenKnockoutWithSeparationConflicts_RoundTrips()
    {
        var participants = Pairs(4, seedRank: i => i, separationKey: i => i is 1 or 4 ? "A" : "B");
        var draw = Generate(Groups(participants, RandomizationPolicy.Seeded(4), groupCount: 2, qualifiers: 1));

        Assert.Equal(2, draw.SeparationConflicts.Count);
        Assert.All(draw.SeparationConflicts, conflict => Assert.Equal(SeparationConflictKind.SameGroup, conflict.Kind));
        Assert.Contains(Knockout(draw).Matches, match => match.Home is GroupPositionSlot);
        AssertRoundTrips(draw);
    }

    [Fact]
    public void Structure_UnknownVersion_IsRejected()
    {
        var node = JsonSerializer.SerializeToNode(TournamentDrawJson.WriteDraw(Generate(SingleElimination(Participants(2)))))!;

        node["version"] = 2;
        var error = Assert.Throws<JsonException>(() => TournamentDrawJson.ReadDraw(ToElement(node)));
        Assert.Contains("version 2", error.Message, StringComparison.Ordinal);

        node.AsObject().Remove("version");
        Assert.Throws<JsonException>(() => TournamentDrawJson.ReadDraw(ToElement(node)));
    }

    [Theory]
    [InlineData("""{"version":1,"format":"knockout"}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomizationMode":"seeded","seed":"-1"}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomizationMode":"seeded","seed":"1"}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomizationMode":"seeded","seed":"1","participants":[],"phases":[{"id":"KO","kind":"knockout","groups":[],"rounds":[{"number":1,"name":"final","restingParticipantIds":[],"matches":[{"id":"M","roundNumber":1,"number":1,"home":{"kind":"court"},"away":{"kind":"bye"}}]}]}],"separationConflicts":[]}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomizationMode":"seeded","seed":"1","participants":[],"phases":[{"id":"KO","kind":"knockout","groups":[],"rounds":[{"number":1,"name":"final","restingParticipantIds":[],"matches":[{"id":"M","roundNumber":1,"number":1,"home":{"kind":"participant"},"away":{"kind":"bye"}}]}]}],"separationConflicts":[]}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomizationMode":"seeded","seed":"1","participants":[],"phases":[{"id":"KO","kind":"knockout","groups":[],"rounds":[{"number":1,"name":"final","restingParticipantIds":[],"matches":[{"id":"M","roundNumber":1,"number":1,"home":{"kind":"groupPosition","groupId":"A"},"away":{"kind":"bye"}}]}]}],"separationConflicts":[]}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomizationMode":"seeded","seed":"1","participants":[],"phases":[],"separationConflicts":[{"key":"A","participantIds":["p1"],"kind":"sameClub","locationId":"A"}]}""")]
    [InlineData("""[]""")]
    public void Structure_MalformedDocument_IsRejected(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<JsonException>(() => TournamentDrawJson.ReadDraw(document.RootElement));
    }

    [Fact]
    public void Request_WithEveryOption_HasTheVersionedShape_AndRoundTrips()
    {
        var request = new DrawRequest(
            DrawFormat.GroupsThenKnockout,
            ParticipationFormat.Team,
            [new DrawParticipant("t1", "Team 1", ["A", "B", "C"], 1, "Norte"), new DrawParticipant("t2", "Team 2")],
            RandomizationPolicy.Random(ulong.MaxValue),
            new DrawOptions(new KnockoutOptions(true), new RoundRobinOptions(2), new GroupStageOptions(GroupSize: 3, QualifiersPerGroup: 1)));
        var scoring = new ScoringRules(5, thirdSetSuperTiebreak: false);

        var json = TournamentDrawJson.WriteRequest(request, scoring);

        Assert.Equal(
            """
            {"version":1,"format":"groupsThenKnockout","participationFormat":"team","randomization":{"mode":"random","seed":"18446744073709551615"},
            "participants":[{"id":"t1","displayName":"Team 1","members":["A","B","C"],"seedRank":1,"separationKey":"Norte"},
            {"id":"t2","displayName":"Team 2","members":[],"seedRank":null,"separationKey":null}],
            "options":{"knockout":{"thirdPlaceMatch":true},"roundRobin":{"legs":2},"groups":{"groupCount":null,"groupSize":3,"qualifiersPerGroup":1}},
            "scoring":{"bestOf":5,"thirdSetSuperTiebreak":false}}
            """.ReplaceLineEndings(string.Empty),
            json.GetRawText());

        var (readRequest, readScoring) = TournamentDrawJson.ReadRequest(json);

        Assert.Equal(Describe(request), Describe(readRequest));
        Assert.Equal((5, false), (readScoring.BestOf, readScoring.ThirdSetSuperTiebreak));
        Assert.Equal(json.GetRawText(), TournamentDrawJson.WriteRequest(readRequest, readScoring).GetRawText());
    }

    [Fact]
    public void Request_WithoutOptionsOrSeed_RoundTripsTheNulls()
    {
        var request = new DrawRequest(
            DrawFormat.SingleElimination,
            ParticipationFormat.Pair,
            Pairs(3),
            new RandomizationPolicy(RandomizationMode.Seeded, null));

        var json = TournamentDrawJson.WriteRequest(request, ScoringRules.Default);
        var (readRequest, readScoring) = TournamentDrawJson.ReadRequest(json);

        Assert.Null(readRequest.Options);
        Assert.Null(readRequest.Randomization.Seed);
        Assert.Equal(Describe(request), Describe(readRequest));
        Assert.Equal((3, true), (readScoring.BestOf, readScoring.ThirdSetSuperTiebreak));
    }

    [Theory]
    [InlineData("""{"version":2,"format":"singleElimination"}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[],"options":null}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[],"options":null,"scoring":{"bestOf":2,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"shuffled","seed":null},"participants":[],"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","participants":[],"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":"x"},"participants":[],"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[{"id":"p1"}],"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":"1","format":"singleElimination"}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":5},"participants":[],"options":null,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[{"id":"p1","displayName":5,"members":[],"seedRank":null,"separationKey":null}],"options":null,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[{"id":"p1","displayName":"A","members":"A","seedRank":null,"separationKey":null}],"options":null,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[{"id":"p1","displayName":"A","members":[1],"seedRank":null,"separationKey":null}],"options":null,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[{"id":"p1","displayName":"A","members":[],"seedRank":"1","separationKey":null}],"options":null,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[{"id":"p1","displayName":"A","members":[],"seedRank":1.5,"separationKey":null}],"options":null,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":{},"options":null,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[],"options":true,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[],"options":{"knockout":true,"roundRobin":null,"groups":null},"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[],"options":{"knockout":{"thirdPlaceMatch":"yes"},"roundRobin":null,"groups":null},"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1,"format":"singleElimination","participationFormat":"pair","randomization":{"mode":"seeded","seed":null},"participants":[],"options":{"knockout":null,"roundRobin":{"legs":null},"groups":null},"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    public void Request_MalformedDocument_IsRejected(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<JsonException>(() => TournamentDrawJson.ReadRequest(document.RootElement));
    }

    [Fact]
    public void Options_HaveTheVersionedShape_AndRoundTrip()
    {
        var options = new DrawOptions(new KnockoutOptions(true), new RoundRobinOptions(1), new GroupStageOptions(GroupCount: 2));

        var json = TournamentDrawJson.WriteOptions(options, new ScoringRules(3, false));

        Assert.Equal(
            """{"version":1,"knockout":{"thirdPlaceMatch":true},"roundRobin":{"legs":1},"groups":{"groupCount":2,"groupSize":null,"qualifiersPerGroup":2},"scoring":{"bestOf":3,"thirdSetSuperTiebreak":false}}""",
            json.GetRawText());
        var (readOptions, readScoring) = TournamentDrawJson.ReadOptions(json);
        Assert.Equal(options, readOptions);
        Assert.False(readScoring.ThirdSetSuperTiebreak);
    }

    [Fact]
    public void Options_Absent_AreStoredAsEmptySections_AndReadAsDefaults()
    {
        var json = TournamentDrawJson.WriteOptions(null, ScoringRules.Default);

        Assert.Equal(
            """{"version":1,"knockout":null,"roundRobin":null,"groups":null,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""",
            json.GetRawText());
        Assert.Equal(new DrawOptions(), TournamentDrawJson.ReadOptions(json).Options);
    }

    [Theory]
    [InlineData("""{"version":3,"scoring":{"bestOf":3,"thirdSetSuperTiebreak":true}}""")]
    [InlineData("""{"version":1}""")]
    public void Options_MalformedDocument_IsRejected(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<JsonException>(() => TournamentDrawJson.ReadOptions(document.RootElement));
    }

    [Fact]
    public void Sets_KeepTheTiebreakObject_AndRoundTrip()
    {
        IReadOnlyList<SetScore> sets = [new(7, 6, new TiebreakScore(7, 5)), new(4, 6), new(10, 8)];

        var json = TournamentDrawJson.WriteSets(sets);

        Assert.Equal(
            """[{"home":7,"away":6,"tiebreak":{"home":7,"away":5}},{"home":4,"away":6,"tiebreak":null},{"home":10,"away":8,"tiebreak":null}]""",
            json.GetRawText());
        Assert.Equal(sets, TournamentDrawJson.ReadSets(json));
        Assert.Empty(TournamentDrawJson.ReadSets(TournamentDrawJson.WriteSets([])));
    }

    [Theory]
    [InlineData("""{"home":6,"away":4}""")]
    [InlineData("""[null]""")]
    [InlineData("""[{"home":7,"away":6,"tiebreak":7}]""")]
    [InlineData("""[{"home":"7","away":6,"tiebreak":null}]""")]
    public void Sets_MalformedDocument_IsRejected(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<JsonException>(() => TournamentDrawJson.ReadSets(document.RootElement));
    }

    private static void AssertRoundTrips(Draw draw)
    {
        var json = TournamentDrawJson.WriteDraw(draw);
        Assert.Equal(TournamentDrawJson.Version, json.GetProperty("version").GetInt32());

        var read = TournamentDrawJson.ReadDraw(json);

        Assert.Equal(Describe(draw), Describe(read));
        Assert.Equal(Snapshot(draw), Snapshot(read));
        Assert.Equal(json.GetRawText(), TournamentDrawJson.WriteDraw(read).GetRawText());
    }

    private static IReadOnlyList<DrawParticipant> Pairs(
        int count,
        Func<int, int?>? seedRank = null,
        Func<int, string?>? separationKey = null) =>
        Enumerable.Range(1, count)
            .Select(i => new DrawParticipant(
                $"p{i}",
                $"Pair {i}",
                [$"Left {i}", $"Right {i}"],
                seedRank?.Invoke(i),
                separationKey?.Invoke(i)))
            .ToList();

    private static JsonElement ToElement(System.Text.Json.Nodes.JsonNode node) => JsonSerializer.SerializeToElement(node);

    /// <summary>Flattens every value of a draw (serializer-independent) so two draws compare deeply.</summary>
    private static IReadOnlyList<string> Describe(Draw draw)
    {
        var lines = new List<string> { $"draw {draw.Format} {draw.ParticipationFormat} {draw.RandomizationMode} {draw.Seed}" };
        lines.AddRange(draw.Participants.Select(Describe));
        foreach (var phase in draw.Phases)
        {
            lines.Add($"phase {phase.Id} {phase.Kind}");
            lines.AddRange(phase.Rounds.SelectMany(round => Describe("round", round)));
            foreach (var group in phase.Groups)
            {
                lines.Add($"group {group.Id} [{string.Join(',', group.ParticipantIds)}]");
                lines.AddRange(group.Rounds.SelectMany(round => Describe($"group {group.Id} round", round)));
            }
        }

        lines.AddRange(draw.SeparationConflicts.Select(conflict =>
            $"conflict {conflict.Key} [{string.Join(',', conflict.ParticipantIds)}] {conflict.Kind} {conflict.LocationId}"));
        return lines;
    }

    private static IEnumerable<string> Describe(string prefix, Round round) =>
        new[] { $"{prefix} {round.Number} {round.Name} rest [{string.Join(',', round.RestingParticipantIds)}]" }
            .Concat(round.Matches.Select(match => $"  {match}"));

    private static string Describe(DrawParticipant participant) =>
        $"participant {participant.Id} {participant.DisplayName} [{string.Join(',', participant.Members)}] " +
        $"{participant.SeedRank?.ToString() ?? "-"} {participant.SeparationKey ?? "-"}";

    private static IReadOnlyList<string> Describe(DrawRequest request) =>
        new[]
        {
            $"request {request.Format} {request.ParticipationFormat} {request.Randomization}",
            $"options {request.Options?.Knockout} {request.Options?.RoundRobin} {request.Options?.Groups}",
        }.Concat(request.Participants.Select(Describe)).ToList();
}
