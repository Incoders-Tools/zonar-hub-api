using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Tests.Application.TournamentDraws;
using static ZonarHub.Tests.Application.TournamentDraws.TournamentDrawBodies;

namespace ZonarHub.Tests.Api.TournamentDraws;

/// <summary>
/// Admin draw endpoints (D5) driven through the real handlers: tenant rule, lifecycle rules, result recording and the
/// D7 JSON contract as serialized by the API (web defaults, no global enum converter).
/// </summary>
public class AdminTournamentDrawEndpointTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> Operations => new()
    {
        "preview", "save", "get", "publish", "unpublish", "delete", "result",
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryEndpoint_AnotherTenantsTournament_IsForbiddenWithoutWriting(string operation)
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.ForeignTournamentWithDrawAsync();

        var result = await InvokeAsync(operation, h, tournament.Id);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(AdminTournamentErrors.Forbidden.MessageKey, problem.ProblemDetails.Detail);
        Assert.Empty(h.Draws.Writes);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryEndpoint_UnknownTournament_IsNotFoundWithoutWriting(string operation)
    {
        var h = new TournamentDrawsTestHarness();
        await h.OwnTournamentAsync();

        var result = await InvokeAsync(operation, h, Guid.NewGuid());

        AssertProblem(result, StatusCodes.Status404NotFound, AdminTournamentErrors.NotFound.Code);
        Assert.Empty(h.Draws.Writes);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("publish")]
    [InlineData("unpublish")]
    [InlineData("delete")]
    [InlineData("result")]
    public async Task DrawEndpoints_TournamentWithoutDraw_AreNotFound(string operation)
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();

        var result = await InvokeAsync(operation, h, tournament.Id);

        AssertProblem(result, StatusCodes.Status404NotFound, "draws.not_found");
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task Preview_WithoutSeed_GeneratesOneAndSavesNothing()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();

        var first = Draw(await AdminTournamentDrawEndpointsExtensions.PreviewAsync(
            tournament.Id, Knockout(8, seed: null), h.Sender, CancellationToken.None));
        var second = Draw(await AdminTournamentDrawEndpointsExtensions.PreviewAsync(
            tournament.Id, Knockout(8, seed: "  "), h.Sender, CancellationToken.None));

        Assert.True(ulong.TryParse(first.Seed, out _));
        Assert.True(ulong.TryParse(second.Seed, out _));
        Assert.NotEqual(first.Seed, second.Seed);
        Assert.Equal("draft", first.Status);
        Assert.Null(first.PublishedAt);
        Assert.Empty(h.Draws.Writes);
        Assert.Null(h.Draws.Find(tournament.Id));
    }

    [Fact]
    public async Task Preview_WithTheReturnedSeed_ReproducesTheDraw()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var generated = Draw(await AdminTournamentDrawEndpointsExtensions.PreviewAsync(
            tournament.Id, Knockout(8, seed: null), h.Sender, CancellationToken.None));

        var replayed = Draw(await AdminTournamentDrawEndpointsExtensions.PreviewAsync(
            tournament.Id, Knockout(8, seed: generated.Seed), h.Sender, CancellationToken.None));

        Assert.Equal(Json(generated), Json(replayed));
    }

    [Theory]
    [InlineData("format", "knockout", "draws.format_not_supported")]
    [InlineData("participationFormat", "duo", "draws.invalid_participation_format")]
    [InlineData("mode", "shuffled", "draws.invalid_randomization_mode")]
    [InlineData("randomization", null, "draws.invalid_randomization_mode")]
    [InlineData("seed", "-1", "draws.invalid_seed")]
    [InlineData("seed", "18446744073709551616", "draws.invalid_seed")]
    [InlineData("seed", "12a", "draws.invalid_seed")]
    [InlineData("participants", null, "draws.participants_min")]
    [InlineData("participant", null, "draws.participant_required")]
    [InlineData("legs", "3", "draws.invalid_round_robin_legs")]
    public async Task PreviewAndSave_InvalidRequest_ReturnValidationProblemWithCodeAndSaveNothing(
        string field,
        string? value,
        string code)
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var valid = field == "legs" ? RoundRobin(4) : Knockout(4);
        var body = field switch
        {
            "format" => valid with { Format = value },
            "participationFormat" => valid with { ParticipationFormat = value },
            "mode" => valid with { Randomization = new DrawRandomizationBody(value, "7") },
            "randomization" => valid with { Randomization = null },
            "seed" => valid with { Randomization = new DrawRandomizationBody("seeded", value) },
            "participants" => valid with { Participants = null },
            "participant" => valid with { Participants = [.. valid.Participants!, null] },
            _ => valid with { Options = new DrawOptionsBody(RoundRobin: new RoundRobinOptionsBody(int.Parse(value!))) },
        };

        var preview = await AdminTournamentDrawEndpointsExtensions.PreviewAsync(tournament.Id, body, h.Sender, CancellationToken.None);
        var save = await AdminTournamentDrawEndpointsExtensions.SaveAsync(tournament.Id, body, h.Sender, CancellationToken.None);

        AssertProblem(preview, StatusCodes.Status400BadRequest, code);
        AssertProblem(save, StatusCodes.Status400BadRequest, code);
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task Save_NewDraw_StoresADraftOfTheTournamentsOrganizationWithTheGeneratedSeed()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.SaveAsync(
            tournament.Id, Knockout(4, seed: null), h.Sender, CancellationToken.None));

        var saved = Assert.Single(h.Draws.SavedDrafts);
        Assert.Equal(tournament.Id, saved.TournamentId);
        Assert.Equal(tournament.OrganizationId, saved.OrganizationId);
        Assert.Null(saved.Request.Randomization.Seed);
        Assert.Equal(saved.Structure.Seed.ToString(), draw.Seed);
        Assert.Equal("draft", draw.Status);
        Assert.True(saved.Scoring.ThirdSetSuperTiebreak);
    }

    [Fact]
    public async Task Save_ScoringOption_IsStoredAndReturned()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var body = Knockout(4) with { Options = new DrawOptionsBody(Scoring: new ScoringOptionsBody(false)) };

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.SaveAsync(tournament.Id, body, h.Sender, CancellationToken.None));

        Assert.False(Assert.Single(h.Draws.SavedDrafts).Scoring.ThirdSetSuperTiebreak);
        Assert.False(draw.Options.Scoring.ThirdSetSuperTiebreak);
        Assert.Equal(3, draw.Options.Scoring.BestOf);
    }

    [Fact]
    public async Task Save_ExistingDrawWithAResult_IsConflictWithoutSaving()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M1"));

        var result = await AdminTournamentDrawEndpointsExtensions.SaveAsync(tournament.Id, Knockout(4, "99"), h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict, "draws.results_exist");
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task Save_ResultRecordedWhileGenerating_IsConflictWithoutSaving()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.ResultRecordedBeforeRecheck = HomeWin("KO-R1-M1");

        var result = await AdminTournamentDrawEndpointsExtensions.SaveAsync(tournament.Id, Knockout(4, "99"), h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict, "draws.results_exist");
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task Save_ExistingDrawWithOnlyScheduledResults_IsReplaced()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var original = h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(tournament.Id, new MatchResult("KO-R1-M1", MatchStatus.Scheduled, courtName: "Court 2"));

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.SaveAsync(tournament.Id, Knockout(6, "99"), h.Sender, CancellationToken.None));

        Assert.Equal(["save"], h.Draws.Writes);
        Assert.Equal(original.Id, h.Draws.Find(tournament.Id)!.Id);
        Assert.Equal("99", draw.Seed);
        Assert.Equal(6, draw.Participants.Count);
    }

    [Fact]
    public async Task Get_ReturnsTheStoredDrawResolvedWithItsResults()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        var winner = await HomeOfAsync(h, tournament.Id, "KO-R1-M1");
        h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M1"));

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.GetAsync(tournament.Id, h.Sender, CancellationToken.None));

        var final = Match(draw, "KO-R2-M1");
        Assert.Equal("winnerOf", final.Home.Kind);
        Assert.Equal("KO-R1-M1", final.Home.MatchId);
        Assert.Equal(winner, final.Home.ResolvedParticipantId);
        Assert.Null(final.Away.ResolvedParticipantId);
        Assert.Equal("completed", Match(draw, "KO-R1-M1").Result!.Status);
    }

    [Fact]
    public async Task Get_IgnoresAStoredResultOfAMatchTheStructureNoLongerHas()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(tournament.Id, HomeWin("KO-R3-M9"));

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.GetAsync(tournament.Id, h.Sender, CancellationToken.None));

        Assert.All(draw.Phases.SelectMany(p => p.Rounds).SelectMany(r => r.Matches), m => Assert.Null(m.Result));
    }

    [Fact]
    public async Task Get_StoredResultThatNoLongerValidates_FailsLoudly()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(tournament.Id, new MatchResult("KO-R1-M1", MatchStatus.Completed, [], MatchSide.Home));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminTournamentDrawEndpointsExtensions.GetAsync(tournament.Id, h.Sender, CancellationToken.None));
    }

    [Fact]
    public async Task PublishThenUnpublish_TransitionTheDrawAndKeepResults()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M1"));

        var published = Draw(await AdminTournamentDrawEndpointsExtensions.PublishAsync(tournament.Id, h.Sender, CancellationToken.None));
        var unpublished = Draw(await AdminTournamentDrawEndpointsExtensions.UnpublishAsync(tournament.Id, h.Sender, CancellationToken.None));

        Assert.Equal("published", published.Status);
        Assert.Equal(TournamentDrawsTestHarness.Now, published.PublishedAt);
        Assert.NotNull(Match(published, "KO-R1-M1").Result);
        Assert.Equal("draft", unpublished.Status);
        Assert.Null(unpublished.PublishedAt);
        Assert.NotNull(Match(unpublished, "KO-R1-M1").Result);
        Assert.Equal(["status:Published", "status:Draft"], h.Draws.Writes);
    }

    [Fact]
    public async Task Publish_AlreadyPublishedDraw_KeepsItsPublicationWithoutWriting()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedStatus(tournament.Id, TournamentDrawStatus.Published);

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.PublishAsync(tournament.Id, h.Sender, CancellationToken.None));

        Assert.Equal("published", draw.Status);
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task Publish_DrawDeletedMeanwhile_IsNotFound()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SimulateConcurrentDelete = true;

        var result = await AdminTournamentDrawEndpointsExtensions.PublishAsync(tournament.Id, h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status404NotFound, "draws.not_found");
    }

    [Fact]
    public async Task Delete_DraftWithOnlyScheduledResults_ReturnsNoContent()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(tournament.Id, new MatchResult("KO-R1-M1", MatchStatus.Scheduled, courtName: "Court 2"));

        var result = await AdminTournamentDrawEndpointsExtensions.DeleteAsync(tournament.Id, h.Sender, CancellationToken.None);

        Assert.IsType<NoContent>(result);
        Assert.Null(h.Draws.Find(tournament.Id));
    }

    [Fact]
    public async Task Delete_PublishedDraw_IsConflictNotDraft()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedStatus(tournament.Id, TournamentDrawStatus.Published);

        var result = await AdminTournamentDrawEndpointsExtensions.DeleteAsync(tournament.Id, h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict, "draws.not_draft");
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task Delete_DrawWithResults_IsConflictResultsExist()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M1"));

        var result = await AdminTournamentDrawEndpointsExtensions.DeleteAsync(tournament.Id, h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict, "draws.results_exist");
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task Delete_DraftPublishedOrDeletedMeanwhile_IsConflictNotDraft()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SimulateConcurrentDelete = true;

        var result = await AdminTournamentDrawEndpointsExtensions.DeleteAsync(tournament.Id, h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict, "draws.not_draft");
    }

    [Fact]
    public async Task RecordResult_Completed_StoresItAndAdvancesTheWinnerToTheNextMatch()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        var winner = await HomeOfAsync(h, tournament.Id, "KO-R1-M1");
        var courtId = Guid.NewGuid();

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, "KO-R1-M1", Completed() with { CourtId = courtId }, h.Sender, CancellationToken.None));

        Assert.Equal(winner, Match(draw, "KO-R2-M1").Home.ResolvedParticipantId);
        var stored = Assert.Single(h.Draws.UpsertedResults);
        Assert.Equal(courtId, stored.CourtId);
        Assert.Equal(MatchStatus.Completed, stored.Result.Status);
        Assert.Equal(MatchSide.Home, stored.Result.WinnerSide);
        Assert.Equal(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), stored.Result.ScheduledAtUtc);
        Assert.Equal(DateTimeKind.Utc, stored.Result.ScheduledAtUtc!.Value.Kind);
        Assert.Equal(new TiebreakScore(7, 5), stored.Result.Sets[1].Tiebreak);
    }

    [Fact]
    public async Task RecordResult_ScheduledWithoutSets_ClearsAnUnlockedResult()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M1"));
        var clear = new MatchResultBody("scheduled", null, null, null, "Court 3", null, null, null);

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, "KO-R1-M1", clear, h.Sender, CancellationToken.None));

        var cleared = Match(draw, "KO-R1-M1").Result!;
        Assert.Equal("scheduled", cleared.Status);
        Assert.Empty(cleared.Sets);
        Assert.Null(cleared.WinnerSide);
        Assert.Equal("Court 3", cleared.CourtName);
        Assert.Null(Match(draw, "KO-R2-M1").Home.ResolvedParticipantId);
    }

    [Theory]
    [InlineData("status", StatusCodes.Status400BadRequest, "draws.results.invalid_status")]
    [InlineData("statusMissing", StatusCodes.Status400BadRequest, "draws.results.invalid_status")]
    [InlineData("winnerSide", StatusCodes.Status400BadRequest, "draws.results.invalid_winner_side")]
    [InlineData("nullSet", StatusCodes.Status400BadRequest, "draws.results.invalid_set")]
    [InlineData("mismatch", StatusCodes.Status400BadRequest, "draws.results.winner_mismatch")]
    [InlineData("unknownMatch", StatusCodes.Status404NotFound, "draws.results.unknown_match")]
    [InlineData("pending", StatusCodes.Status409Conflict, "draws.results.participants_pending")]
    [InlineData("bye", StatusCodes.Status409Conflict, "draws.results.bye_match")]
    [InlineData("locked", StatusCodes.Status409Conflict, "draws.results.locked_by_next_match")]
    public async Task RecordResult_RejectedResult_MapsTheDomainCodeWithoutWriting(string scenario, int status, string code)
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(scenario == "bye" ? 3 : 4));
        if (scenario == "locked")
        {
            h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M1"));
            h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M2"));
            h.Draws.SeedResult(tournament.Id, HomeWin("KO-R2-M1"));
        }

        var byeMatch = scenario == "bye" ? await ByeMatchAsync(h, tournament.Id) : null;
        var (matchId, body) = scenario switch
        {
            "status" => ("KO-R1-M1", Completed() with { Status = "done" }),
            "statusMissing" => ("KO-R1-M1", Completed() with { Status = null }),
            "winnerSide" => ("KO-R1-M1", Completed() with { WinnerSide = "left" }),
            "nullSet" => ("KO-R1-M1", Completed() with { Sets = [null] }),
            "mismatch" => ("KO-R1-M1", Completed() with { WinnerSide = "away" }),
            "unknownMatch" => ("KO-R9-M9", Completed()),
            "pending" => ("KO-R2-M1", Completed()),
            "bye" => (byeMatch!, Completed()),
            _ => ("KO-R1-M1", Completed("away")),
        };

        var result = await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, matchId, body, h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task DrawJson_UsesTheD7PropertyNamesAndCamelCaseEnums()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4, separationKey: "Club Norte"));
        await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, "KO-R1-M1", Completed(), h.Sender, CancellationToken.None);
        await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id,
            "KO-R1-M2",
            new MatchResultBody("inProgress", [new SetScoreBody(3, 2, null)], null, null, null, null,
                new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero), null),
            h.Sender,
            CancellationToken.None);

        using var json = JsonDocument.Parse(Json(Draw(
            await AdminTournamentDrawEndpointsExtensions.GetAsync(tournament.Id, h.Sender, CancellationToken.None))));
        var root = json.RootElement;

        Assert.Equal(
            ["status", "format", "participationFormat", "randomizationMode", "seed", "publishedAt", "options", "participants", "phases", "separationConflicts"],
            Names(root));
        Assert.Equal("draft", root.GetProperty("status").GetString());
        Assert.Equal("singleElimination", root.GetProperty("format").GetString());
        Assert.Equal("pair", root.GetProperty("participationFormat").GetString());
        Assert.Equal("seeded", root.GetProperty("randomizationMode").GetString());
        Assert.Equal("7", root.GetProperty("seed").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("publishedAt").ValueKind);
        Assert.Equal(["knockout", "roundRobin", "groups", "scoring"], Names(root.GetProperty("options")));
        Assert.False(root.GetProperty("options").GetProperty("knockout").GetProperty("thirdPlaceMatch").GetBoolean());
        Assert.Equal(["bestOf", "thirdSetSuperTiebreak"], Names(root.GetProperty("options").GetProperty("scoring")));

        var participants = root.GetProperty("participants").EnumerateArray().ToList();
        Assert.Equal(["id", "displayName", "members", "seedRank", "separationKey"], Names(participants[0]));
        Assert.Equal("Club Norte", participants[0].GetProperty("separationKey").GetString());
        Assert.Equal(["id", "displayName", "members", "separationKey"], Names(participants[1]));

        var phase = root.GetProperty("phases")[0];
        Assert.Equal(["id", "kind", "groups", "rounds"], Names(phase));
        Assert.Equal("knockout", phase.GetProperty("kind").GetString());
        Assert.Equal(0, phase.GetProperty("groups").GetArrayLength());
        var round = phase.GetProperty("rounds")[0];
        Assert.Equal(["number", "name", "restingParticipantIds", "matches"], Names(round));
        Assert.Equal("semifinal", round.GetProperty("name").GetString());

        var completed = round.GetProperty("matches")[0];
        Assert.Equal(["id", "roundNumber", "number", "home", "away", "winnerAdvancesTo", "loserAdvancesTo", "result"], Names(completed));
        Assert.Equal(["kind", "participantId", "resolvedParticipantId"], Names(completed.GetProperty("home")));
        Assert.Equal("participant", completed.GetProperty("home").GetProperty("kind").GetString());
        var result = completed.GetProperty("result");
        Assert.Equal(["status", "sets", "winnerSide", "courtName", "scheduledAt", "startedAt", "finishedAt"], Names(result));
        Assert.Equal("completed", result.GetProperty("status").GetString());
        Assert.Equal("home", result.GetProperty("winnerSide").GetString());
        Assert.Equal("2026-11-02T12:00:00Z", result.GetProperty("scheduledAt").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("sets")[0].GetProperty("tiebreak").ValueKind);
        Assert.Equal(7, result.GetProperty("sets")[1].GetProperty("tiebreak").GetProperty("home").GetInt32());
        Assert.Equal(5, result.GetProperty("sets")[1].GetProperty("tiebreak").GetProperty("away").GetInt32());

        var inProgress = round.GetProperty("matches")[1].GetProperty("result");
        Assert.Equal("inProgress", inProgress.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, inProgress.GetProperty("winnerSide").ValueKind);
        Assert.Equal("2026-11-02T10:00:00Z", inProgress.GetProperty("startedAt").GetString());

        var final = phase.GetProperty("rounds")[1].GetProperty("matches")[0];
        Assert.Equal(["kind", "matchId", "resolvedParticipantId"], Names(final.GetProperty("home")));
        Assert.Equal("winnerOf", final.GetProperty("home").GetProperty("kind").GetString());
        Assert.Equal(["kind", "matchId"], Names(final.GetProperty("away")));
        Assert.Equal(JsonValueKind.Null, final.GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Null, final.GetProperty("winnerAdvancesTo").ValueKind);

        var conflict = root.GetProperty("separationConflicts")[0];
        Assert.Equal(["key", "participantIds", "kind", "locationId"], Names(conflict));
        Assert.Equal("firstRoundMatch", conflict.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task RoundRobinJson_CarriesItsStandingsAsTheSingleGroupRR()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();

        using var json = JsonDocument.Parse(Json(Draw(await AdminTournamentDrawEndpointsExtensions.PreviewAsync(
            tournament.Id, RoundRobin(3), h.Sender, CancellationToken.None))));

        var phase = json.RootElement.GetProperty("phases")[0];
        Assert.Equal("roundRobin", phase.GetProperty("kind").GetString());
        Assert.Equal("random", json.RootElement.GetProperty("randomizationMode").GetString());
        var group = Assert.Single(phase.GetProperty("groups").EnumerateArray());
        Assert.Equal(["id", "participantIds", "standings"], Names(group));
        Assert.Equal("RR", group.GetProperty("id").GetString());
        Assert.Equal(["p1", "p2", "p3"], group.GetProperty("participantIds").EnumerateArray().Select(e => e.GetString()));
        var standings = group.GetProperty("standings").EnumerateArray().ToList();
        Assert.Equal(3, standings.Count);
        Assert.Equal(
            ["participantId", "played", "won", "lost", "setsFor", "setsAgainst", "gamesFor", "gamesAgainst", "position"],
            Names(standings[0]));
        Assert.Equal([1, 2, 3], standings.Select(s => s.GetProperty("position").GetInt32()));
        Assert.Contains(phase.GetProperty("rounds").EnumerateArray(), r => r.GetProperty("restingParticipantIds").GetArrayLength() == 1);
    }

    [Fact]
    public async Task GroupStageJson_CarriesEachGroupsStandingsAndGroupPositionSlots()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.PreviewAsync(
            tournament.Id, Groups(8), h.Sender, CancellationToken.None));

        var groupStage = draw.Phases[0];
        Assert.Equal("groupStage", groupStage.Kind);
        Assert.Equal(["A", "B"], groupStage.Groups.Select(g => g.Id));
        Assert.All(groupStage.Groups, g => Assert.Equal(4, g.Standings.Count));
        Assert.Equal(groupStage.Groups[0].ParticipantIds.Order(), groupStage.Groups[0].Standings.Select(s => s.ParticipantId).Order());
        var firstSlot = draw.Phases[1].Rounds[0].Matches[0].Home;
        Assert.Equal("groupPosition", firstSlot.Kind);
        Assert.NotNull(firstSlot.GroupId);
        Assert.NotNull(firstSlot.Rank);
        Assert.Null(firstSlot.ResolvedParticipantId);
    }

    [Fact]
    public async Task Preview_ThirdPlaceMatch_EmitsLoserOfSlots()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var body = Knockout(4) with { Options = new DrawOptionsBody(Knockout: new KnockoutOptionsBody(true)) };

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.PreviewAsync(tournament.Id, body, h.Sender, CancellationToken.None));

        var thirdPlace = draw.Phases[0].Rounds[1].Matches[1];
        Assert.Equal("loserOf", thirdPlace.Home.Kind);
        Assert.Equal("KO-R1-M1", thirdPlace.Home.MatchId);
        Assert.Equal(thirdPlace.Id, Match(draw, "KO-R1-M1").LoserAdvancesTo);
        Assert.True(draw.Options.Knockout!.ThirdPlaceMatch);
    }

    [Fact]
    public async Task Preview_WithoutOptions_UsesTheEngineAndScoringDefaults()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.PreviewAsync(
            tournament.Id, Knockout(4) with { Options = null }, h.Sender, CancellationToken.None));

        Assert.Null(draw.Options.Knockout);
        Assert.Null(draw.Options.RoundRobin);
        Assert.Null(draw.Options.Groups);
        Assert.Equal(new ScoringOptionsResponse(3, true), draw.Options.Scoring);
        Assert.Equal(2, draw.Phases[0].Rounds.Count);
    }

    [Fact]
    public async Task RecordResult_BlankCourtName_IsStoredAsNoCourt()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, "KO-R1-M1", Completed() with { CourtName = "  " }, h.Sender, CancellationToken.None));

        Assert.Null(Assert.Single(h.Draws.UpsertedResults).Result.CourtName);
        Assert.Null(Match(draw, "KO-R1-M1").Result!.CourtName);
    }

    [Fact]
    public async Task PublicAudience_DropsSeparationKeysAndConflicts()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var record = h.Draws.SeedDraft(tournament, Knockout(4, separationKey: "Club Norte"));
        var resolved = DrawProgression.Resolve(record.Structure, [], record.Scoring).Value;

        var draw = TournamentDrawResponseMapper.ToResponse(
            resolved, TournamentDrawStatus.Published, TournamentDrawsTestHarness.Now, record.Options, TournamentDrawAudience.Public);

        Assert.All(draw.Participants, p => Assert.Null(p.SeparationKey));
        Assert.Empty(draw.SeparationConflicts);
        Assert.DoesNotContain("separationKey", Json(draw));
        Assert.Equal("2026-10-31T12:00:00Z", JsonDocument.Parse(Json(draw)).RootElement.GetProperty("publishedAt").GetString());
    }

    [Fact]
    public void DrawEndpoints_AreMappedUnderTheTournamentAndRequireTheAdminPolicy()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton(new TournamentDrawsTestHarness().Sender);
        var app = builder.Build();
        app.MapAdminTournamentDrawEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        const string Route = "/api/admin/tournaments/{tournamentId:guid}/draw";
        Assert.Equal(
            [
                $"DELETE {Route}/",
                $"GET {Route}/",
                $"POST {Route}/preview",
                $"POST {Route}/publish",
                $"POST {Route}/unpublish",
                $"PUT {Route}/",
                $"PUT {Route}/matches/{{matchId}}/result",
            ],
            endpoints
                .Select(e => $"{e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single()} {e.RoutePattern.RawText}")
                .Order(StringComparer.Ordinal));
        foreach (var endpoint in endpoints)
        {
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == "AdminOrAbove");
        }
    }

    private static Task<IResult> InvokeAsync(string operation, TournamentDrawsTestHarness h, Guid tournamentId) => operation switch
    {
        "preview" => AdminTournamentDrawEndpointsExtensions.PreviewAsync(tournamentId, Knockout(4), h.Sender, CancellationToken.None),
        "save" => AdminTournamentDrawEndpointsExtensions.SaveAsync(tournamentId, Knockout(4), h.Sender, CancellationToken.None),
        "get" => AdminTournamentDrawEndpointsExtensions.GetAsync(tournamentId, h.Sender, CancellationToken.None),
        "publish" => AdminTournamentDrawEndpointsExtensions.PublishAsync(tournamentId, h.Sender, CancellationToken.None),
        "unpublish" => AdminTournamentDrawEndpointsExtensions.UnpublishAsync(tournamentId, h.Sender, CancellationToken.None),
        "delete" => AdminTournamentDrawEndpointsExtensions.DeleteAsync(tournamentId, h.Sender, CancellationToken.None),
        _ => AdminTournamentDrawEndpointsExtensions.RecordResultAsync(tournamentId, "KO-R1-M1", Completed(), h.Sender, CancellationToken.None),
    };

    private static MatchResult HomeWin(string matchId) =>
        new(matchId, MatchStatus.Completed, [new SetScore(6, 4), new SetScore(6, 4)], MatchSide.Home);

    private static async Task<string> HomeOfAsync(TournamentDrawsTestHarness h, Guid tournamentId, string matchId)
    {
        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.GetAsync(tournamentId, h.Sender, CancellationToken.None));
        h.Draws.Writes.Clear();
        return Match(draw, matchId).Home.ParticipantId!;
    }

    private static async Task<string> ByeMatchAsync(TournamentDrawsTestHarness h, Guid tournamentId)
    {
        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.GetAsync(tournamentId, h.Sender, CancellationToken.None));
        return draw.Phases.SelectMany(p => p.Rounds).SelectMany(r => r.Matches)
            .First(m => m.Home.Kind == "bye" || m.Away.Kind == "bye").Id;
    }

    private static TournamentDrawResponse Draw(IResult result) =>
        Assert.IsType<Ok<TournamentDrawResponse>>(result).Value!;

    private static DrawMatchResponse Match(TournamentDrawResponse draw, string matchId) =>
        draw.Phases.SelectMany(p => p.Rounds).SelectMany(r => r.Matches).Single(m => m.Id == matchId);

    private static string Json(TournamentDrawResponse draw) => JsonSerializer.Serialize(draw, Web);

    private static List<string> Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).ToList();

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }
}
