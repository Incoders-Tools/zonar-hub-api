using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Draws;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Infrastructure.DependencyInjection;
using ZonarHub.Infrastructure.Persistence.Supabase;
using ZonarHub.Infrastructure.Persistence.Supabase.Draws;
using ZonarHub.Tests.Common;
using static ZonarHub.Tests.Domain.Draws.DrawTestData;

namespace ZonarHub.Tests.Infrastructure.Persistence;

/// <summary>
/// PostgREST side of tournament draws (D2): one call per operation, snake_case columns, versioned jsonb documents,
/// UTC timestamps, and store failures surfaced as <see cref="HttpRequestException"/>.
/// </summary>
public class TournamentDrawRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 12, 30, 0, DateTimeKind.Utc);
    private static readonly Guid DrawId = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid TournamentId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid CourtId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    [Fact]
    public async Task GetByTournamentId_ReadsTheDrawAndItsResultsInOneCall_AndMapsThem()
    {
        var requests = new List<Recorded>();
        var draw = SampleDraw();
        var repository = CreateRepository(requests, _ => Json($"[{DrawRowJson(draw, results: $"[{ResultRowJson()}]")}]"));

        var snapshot = await repository.GetByTournamentIdAsync(TournamentId);

        var request = Assert.Single(requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/rest/v1/tournament_draws", request.Path);
        Assert.Equal(
            ["select=*,results:tournament_draw_match_results(*)", $"tournament_id=eq.{TournamentId}", "results.order=match_id.asc", "limit=1"],
            request.Query);

        var record = snapshot!.Draw;
        Assert.Equal((DrawId, TournamentId, OrganizationId, TournamentDrawStatus.Published), (record.Id, record.TournamentId, record.OrganizationId, record.Status));
        Assert.Equal(6, record.Revision);
        Assert.Equal(new TournamentDrawVersion(DrawId, 6), record.Version);
        Assert.Equal(Snapshot(draw), Snapshot(record.Structure));
        Assert.Equal(draw.Seed, record.Structure.Seed);
        Assert.Equal(DrawFormat.SingleElimination, record.Request.Format);
        Assert.Equal(["p1", "p2", "p3", "p4"], record.Request.Participants.Select(p => p.Id));
        Assert.Null(record.Request.Randomization.Seed);
        Assert.Equal(new DrawOptions(Knockout: new KnockoutOptions(true)), record.Options);
        Assert.Equal((3, false), (record.Scoring.BestOf, record.Scoring.ThirdSetSuperTiebreak));
        AssertUtc(new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc), record.PublishedAtUtc!.Value);
        AssertUtc(new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc), record.CreatedAtUtc);
        AssertUtc(new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc), record.UpdatedAtUtc);

        AssertSampleResult(Assert.Single(snapshot.Results), CourtId);
    }

    [Fact]
    public async Task GetByTournamentId_NoDraw_ReturnsNull()
    {
        var repository = CreateRepository([], _ => Json("[]"));

        Assert.Null(await repository.GetByTournamentIdAsync(TournamentId));
    }

    [Fact]
    public async Task GetByTournamentId_DraftWithoutResults_MapsAnEmptyListAndNoPublishedAt()
    {
        var repository = CreateRepository([], _ => Json($"[{DrawRowJson(SampleDraw(), status: "draft", publishedAt: null, results: "[]")}]"));

        var snapshot = await repository.GetByTournamentIdAsync(TournamentId);

        Assert.Equal(TournamentDrawStatus.Draft, snapshot!.Draw.Status);
        Assert.Null(snapshot.Draw.PublishedAtUtc);
        Assert.Empty(snapshot.Results);
    }

    [Fact]
    public async Task Reads_RowsWithoutEmbeddedResults_MapNoResults()
    {
        var repository = CreateRepository([], _ => Json($"[{DrawRowJson(SampleDraw())}]"));

        Assert.Empty((await repository.GetByTournamentIdAsync(TournamentId))!.Results);
        Assert.Empty((await repository.GetPublishedByTournamentIdAsync(TournamentId))!.Results);
    }

    [Fact]
    public async Task Reads_NullBody_IsTreatedAsNoRows()
    {
        var repository = CreateRepository([], _ => Json("null"));

        Assert.Null(await repository.GetByTournamentIdAsync(TournamentId));
        Assert.Null(await repository.GetPublishedByTournamentIdAsync(TournamentId));
    }

    [Fact]
    public async Task GetByTournamentId_StoredValuesOutsideTheContract_AreRejected()
    {
        var unknownStatus = CreateRepository([], _ => Json($"[{DrawRowJson(SampleDraw(), status: "archived", results: "[]")}]"));
        var unknownResultStatus = CreateRepository([], _ => Json(
            $"[{DrawRowJson(SampleDraw(), results: $"[{ResultRowJson().Replace("\"completed\"", "\"paused\"")}]")}]"));
        var unknownWinner = CreateRepository([], _ => Json(
            $"[{DrawRowJson(SampleDraw(), results: $"[{ResultRowJson().Replace("\"home\"", "\"left\"")}]")}]"));

        await Assert.ThrowsAsync<JsonException>(() => unknownStatus.GetByTournamentIdAsync(TournamentId));
        await Assert.ThrowsAsync<JsonException>(() => unknownResultStatus.GetByTournamentIdAsync(TournamentId));
        await Assert.ThrowsAsync<JsonException>(() => unknownWinner.GetByTournamentIdAsync(TournamentId));
    }

    [Fact]
    public async Task SaveDraft_IsOneRpcCall_WithSnakeCaseParamsTheExpectedVersionAndVersionedDocuments()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, request => EchoSavedDraw(request, revision: 5));
        var draw = SampleDraw();
        var request = SampleRequest();
        var scoring = new ScoringRules(3, thirdSetSuperTiebreak: false);

        var saved = await repository.SaveDraftAsync(
            new TournamentDrawDraft(TournamentId, OrganizationId, request, scoring, draw),
            new TournamentDrawVersion(DrawId, 4));

        var sent = Assert.Single(requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/rest/v1/rpc/save_tournament_draw_draft", sent.Path);
        Assert.Empty(sent.Query);

        var body = JsonNode.Parse(sent.Body!)!.AsObject();
        Assert.Equal(
            ["p_tournament_id", "p_organization_id", "p_expected_draw_id", "p_expected_revision", "p_format",
                "p_participation_format", "p_randomization_mode", "p_seed", "p_options", "p_request", "p_structure", "p_now_utc"],
            body.Select(property => property.Key));
        Assert.Equal(TournamentId.ToString(), body["p_tournament_id"]!.GetValue<string>());
        Assert.Equal(OrganizationId.ToString(), body["p_organization_id"]!.GetValue<string>());
        Assert.Equal(DrawId.ToString(), body["p_expected_draw_id"]!.GetValue<string>());
        Assert.Equal(4, body["p_expected_revision"]!.GetValue<int>());
        Assert.Equal("singleElimination", body["p_format"]!.GetValue<string>());
        Assert.Equal("pair", body["p_participation_format"]!.GetValue<string>());
        Assert.Equal("seeded", body["p_randomization_mode"]!.GetValue<string>());
        Assert.Equal(draw.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), body["p_seed"]!.GetValue<string>());
        Assert.Equal(TournamentDrawJson.WriteOptions(request.Options, scoring).GetRawText(), body["p_options"]!.ToJsonString());
        Assert.Equal(TournamentDrawJson.WriteRequest(request, scoring).GetRawText(), body["p_request"]!.ToJsonString());
        Assert.Equal(TournamentDrawJson.WriteDraw(draw).GetRawText(), body["p_structure"]!.ToJsonString());
        AssertUtc(Now, body["p_now_utc"]!.GetValue<DateTime>().ToUniversalTime());

        Assert.Equal((DrawId, TournamentDrawStatus.Draft, 5), (saved.Id, saved.Status, saved.Revision));
        Assert.Equal(new TournamentDrawVersion(DrawId, 5), saved.Version);
        Assert.Equal(Snapshot(draw), Snapshot(saved.Structure));
        Assert.False(saved.Scoring.ThirdSetSuperTiebreak);
        Assert.Null(saved.PublishedAtUtc);
        AssertUtc(Now, saved.UpdatedAtUtc);
    }

    [Fact]
    public async Task SaveDraft_NoDrawExpected_SendsNullExpectedIdAndRevision()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, request => EchoSavedDraw(request, revision: 0));

        var saved = await repository.SaveDraftAsync(
            new TournamentDrawDraft(TournamentId, OrganizationId, SampleRequest(), ScoringRules.Default, SampleDraw()),
            expected: null);

        var body = JsonNode.Parse(Assert.Single(requests).Body!)!.AsObject();
        foreach (var parameter in new[] { "p_expected_draw_id", "p_expected_revision" })
        {
            Assert.True(body.ContainsKey(parameter), parameter);
            Assert.Null(body[parameter]);
        }

        Assert.Equal(0, saved.Revision);
    }

    [Theory]
    [InlineData(TournamentDrawStatus.Published, "published")]
    [InlineData(TournamentDrawStatus.Draft, "draft")]
    public async Task SetStatus_IsOneRpcCall_WithTheExpectedRevisionAndTheClock(TournamentDrawStatus status, string wire)
    {
        var requests = new List<Recorded>();
        var publishedAt = status == TournamentDrawStatus.Published ? "2026-10-31T12:30:00+00:00" : null;
        var repository = CreateRepository(requests, _ => Json(DrawRowJson(SampleDraw(), status: wire, publishedAt: publishedAt, revision: 8)));

        var updated = await repository.SetStatusAsync(new TournamentDrawVersion(DrawId, 7), status);

        var sent = Assert.Single(requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/rest/v1/rpc/set_tournament_draw_status", sent.Path);
        Assert.Empty(sent.Query);
        var body = JsonNode.Parse(sent.Body!)!.AsObject();
        Assert.Equal(["p_draw_id", "p_expected_revision", "p_status", "p_now_utc"], body.Select(property => property.Key));
        Assert.Equal(DrawId.ToString(), body["p_draw_id"]!.GetValue<string>());
        Assert.Equal(7, body["p_expected_revision"]!.GetValue<int>());
        Assert.Equal(wire, body["p_status"]!.GetValue<string>());
        AssertUtc(Now, body["p_now_utc"]!.GetValue<DateTime>().ToUniversalTime());

        Assert.Equal((status, 8), (updated.Status, updated.Revision));
        Assert.Equal(status == TournamentDrawStatus.Published ? Now : null, updated.PublishedAtUtc);
    }

    [Fact]
    public async Task DeleteDraft_IsOneRpcCall_WithTheExpectedRevision()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, _ => Json($$"""{"id":"{{DrawId}}","revision":3}"""));

        await repository.DeleteDraftAsync(new TournamentDrawVersion(DrawId, 2));

        var sent = Assert.Single(requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/rest/v1/rpc/delete_tournament_draw_draft", sent.Path);
        Assert.Empty(sent.Query);
        var body = JsonNode.Parse(sent.Body!)!.AsObject();
        Assert.Equal(["p_draw_id", "p_expected_revision"], body.Select(property => property.Key));
        Assert.Equal(DrawId.ToString(), body["p_draw_id"]!.GetValue<string>());
        Assert.Equal(2, body["p_expected_revision"]!.GetValue<int>());
    }

    [Fact]
    public async Task UpsertResult_IsOneRpcCall_WithSnakeCaseParamsAndReturnsTheNewRevision()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, request => EchoResult(request, revision: 10));
        var result = SampleResult();

        var stored = await repository.UpsertResultAsync(new TournamentDrawVersion(DrawId, 9), result, CourtId);

        var sent = Assert.Single(requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/rest/v1/rpc/upsert_tournament_draw_match_result", sent.Path);
        Assert.Empty(sent.Query);
        var body = JsonNode.Parse(sent.Body!)!.AsObject();
        Assert.Equal(
            ["p_draw_id", "p_expected_revision", "p_match_id", "p_status", "p_sets", "p_winner_side", "p_court_id",
                "p_court_name", "p_scheduled_at_utc", "p_started_at_utc", "p_finished_at_utc", "p_now_utc"],
            body.Select(property => property.Key));
        Assert.Equal(DrawId.ToString(), body["p_draw_id"]!.GetValue<string>());
        Assert.Equal(9, body["p_expected_revision"]!.GetValue<int>());
        Assert.Equal("KO-R1-M1", body["p_match_id"]!.GetValue<string>());
        Assert.Equal("completed", body["p_status"]!.GetValue<string>());
        Assert.Equal(
            """[{"home":7,"away":6,"tiebreak":{"home":7,"away":4}},{"home":3,"away":6,"tiebreak":null},{"home":10,"away":8,"tiebreak":null}]""",
            body["p_sets"]!.ToJsonString());
        Assert.Equal("home", body["p_winner_side"]!.GetValue<string>());
        Assert.Equal(CourtId.ToString(), body["p_court_id"]!.GetValue<string>());
        Assert.Equal("Central", body["p_court_name"]!.GetValue<string>());
        AssertUtc(new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc), body["p_scheduled_at_utc"]!.GetValue<DateTime>().ToUniversalTime());
        AssertUtc(new DateTime(2026, 10, 31, 9, 5, 0, DateTimeKind.Utc), body["p_started_at_utc"]!.GetValue<DateTime>().ToUniversalTime());
        AssertUtc(new DateTime(2026, 10, 31, 10, 35, 0, DateTimeKind.Utc), body["p_finished_at_utc"]!.GetValue<DateTime>().ToUniversalTime());
        AssertUtc(Now, body["p_now_utc"]!.GetValue<DateTime>().ToUniversalTime());

        Assert.Equal(10, stored.Revision);
        AssertSampleResult(stored.Result, CourtId, Now);
    }

    [Fact]
    public async Task UpsertResult_ScheduledWithoutDetails_SendsNullsAndAnEmptySetList()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, request => EchoResult(request, revision: 1));

        var stored = await repository.UpsertResultAsync(
            new TournamentDrawVersion(DrawId, 0), new MatchResult("RR-R1-M1", MatchStatus.Scheduled), courtId: null);

        var body = JsonNode.Parse(Assert.Single(requests).Body!)!.AsObject();
        Assert.Equal("scheduled", body["p_status"]!.GetValue<string>());
        Assert.Equal("[]", body["p_sets"]!.ToJsonString());
        foreach (var parameter in new[] { "p_winner_side", "p_court_id", "p_court_name", "p_scheduled_at_utc", "p_started_at_utc", "p_finished_at_utc" })
        {
            Assert.True(body.ContainsKey(parameter), parameter);
            Assert.Null(body[parameter]);
        }

        var row = stored.Result;
        Assert.Equal((MatchStatus.Scheduled, null, null), (row.Result.Status, row.Result.WinnerSide, row.CourtId));
        Assert.Empty(row.Result.Sets);
    }

    [Fact]
    public async Task UpsertResult_LocalTimesAreConverted_AndUnspecifiedTimesAreTakenAsUtc()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, request => EchoResult(request, revision: 1));
        var scheduled = new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc);
        var result = new MatchResult(
            "RR-R1-M1",
            MatchStatus.Scheduled,
            scheduledAtUtc: scheduled.ToLocalTime(),
            startedAtUtc: new DateTime(2026, 10, 31, 9, 5, 0, DateTimeKind.Unspecified));

        var stored = await repository.UpsertResultAsync(new TournamentDrawVersion(DrawId, 0), result, courtId: null);

        var body = JsonNode.Parse(Assert.Single(requests).Body!)!.AsObject();
        Assert.Equal("2026-10-31T09:00:00Z", body["p_scheduled_at_utc"]!.GetValue<string>());
        Assert.Equal("2026-10-31T09:05:00Z", body["p_started_at_utc"]!.GetValue<string>());
        AssertUtc(scheduled, stored.Result.Result.ScheduledAtUtc!.Value);
        AssertUtc(new DateTime(2026, 10, 31, 9, 5, 0, DateTimeKind.Utc), stored.Result.Result.StartedAtUtc!.Value);
    }

    public static TheoryData<string, string, TournamentDrawWriteConflict> WriteConflicts()
    {
        var data = new TheoryData<string, string, TournamentDrawWriteConflict>();
        foreach (var operation in new[] { "save", "status", "delete", "result" })
        {
            data.Add(operation, "draws.concurrent_modification", TournamentDrawWriteConflict.ConcurrentModification);
            data.Add(operation, "draws.results_exist", TournamentDrawWriteConflict.ResultsExist);
            data.Add(operation, "draws.not_draft", TournamentDrawWriteConflict.NotDraft);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WriteConflicts))]
    public async Task Writes_RejectedByTheStore_ThrowTheTypedConflict(string operation, string message, TournamentDrawWriteConflict expected)
    {
        var repository = CreateRepository([], _ => Problem(HttpStatusCode.Conflict, "PT409", message));

        var error = await Assert.ThrowsAsync<TournamentDrawConcurrencyException>(() => WriteAsync(repository, operation));

        Assert.Equal(expected, error.Conflict);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("status")]
    [InlineData("delete")]
    [InlineData("result")]
    public async Task Writes_OtherFailures_StayHttpRequestExceptions(string operation)
    {
        var unknownMessage = CreateRepository([], _ => Problem(HttpStatusCode.Conflict, "PT409", "draws.something_else"));
        var otherCode = CreateRepository([], _ => Problem(HttpStatusCode.Conflict, "23505", "draws.concurrent_modification"));
        var badRequest = CreateRepository([], _ => Problem(HttpStatusCode.BadRequest, "PT409", "draws.concurrent_modification"));
        var notJson = CreateRepository([], _ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("conflict") });
        var nullBody = CreateRepository([], _ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("null") });

        await Assert.ThrowsAsync<HttpRequestException>(() => WriteAsync(unknownMessage, operation));
        await Assert.ThrowsAsync<HttpRequestException>(() => WriteAsync(otherCode, operation));
        await Assert.ThrowsAsync<HttpRequestException>(() => WriteAsync(badRequest, operation));
        await Assert.ThrowsAsync<HttpRequestException>(() => WriteAsync(notJson, operation));
        await Assert.ThrowsAsync<HttpRequestException>(() => WriteAsync(nullBody, operation));
    }

    [Theory]
    [InlineData("save")]
    [InlineData("status")]
    [InlineData("result")]
    public async Task Writes_EmptyRpcResult_AreRejected(string operation)
    {
        var repository = CreateRepository([], _ => Json("null"));

        await Assert.ThrowsAsync<JsonException>(() => WriteAsync(repository, operation));
    }

    [Fact]
    public void Infrastructure_RegistersTheSupabaseDrawRepository()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://synthetic.supabase.test",
                ["Supabase:Key"] = "sb_secret_synthetic-test-value",
            })
            .Build();
        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<TournamentDrawRepository>(scope.ServiceProvider.GetRequiredService<ITournamentDrawRepository>());
    }

    [Fact]
    public async Task GetPublished_FiltersPublishedInTheStore_AndNeverSelectsAdminOnlyColumns()
    {
        var requests = new List<Recorded>();
        var draw = SampleDraw();
        var row = DrawRowJson(draw, results: $"[{ResultRowJson(withCourtId: false)}]");
        var repository = CreateRepository(requests, _ => Json($"[{row}]"));

        var published = await repository.GetPublishedByTournamentIdAsync(TournamentId);

        var sent = Assert.Single(requests);
        Assert.Equal("/rest/v1/tournament_draws", sent.Path);
        Assert.Equal(
            [
                "select=tournament_id,options,structure,published_at_utc,updated_at_utc,results:tournament_draw_match_results(" +
                "match_id,status,sets,winner_side,court_name,scheduled_at_utc,started_at_utc,finished_at_utc,updated_at_utc)",
                $"tournament_id=eq.{TournamentId}",
                "status=eq.published",
                "results.order=match_id.asc",
                "limit=1",
            ],
            sent.Query);

        Assert.Equal(TournamentId, published!.TournamentId);
        Assert.Equal(Snapshot(draw), Snapshot(published.Structure));
        Assert.Equal(new DrawOptions(Knockout: new KnockoutOptions(true)), published.Options);
        Assert.False(published.Scoring.ThirdSetSuperTiebreak);
        AssertUtc(new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc), published.PublishedAtUtc);
        AssertUtc(new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc), published.UpdatedAtUtc);
        AssertSampleResult(Assert.Single(published.Results), courtId: null);
    }

    [Fact]
    public async Task GetPublished_NoPublishedDraw_ReturnsNull()
    {
        var repository = CreateRepository([], _ => Json("[]"));

        Assert.Null(await repository.GetPublishedByTournamentIdAsync(TournamentId));
    }

    [Fact]
    public async Task StoreFailure_ThrowsHttpRequestException_ForEveryOperation()
    {
        var repository = CreateRepository([], _ => Problem(HttpStatusCode.Conflict, "23503", "insert or update violates foreign key constraint"));

        await Assert.ThrowsAsync<HttpRequestException>(() => repository.GetByTournamentIdAsync(TournamentId));
        foreach (var operation in new[] { "save", "status", "delete", "result" })
            await Assert.ThrowsAsync<HttpRequestException>(() => WriteAsync(repository, operation));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.GetPublishedByTournamentIdAsync(TournamentId));
    }

    private static Task WriteAsync(TournamentDrawRepository repository, string operation) => operation switch
    {
        "save" => repository.SaveDraftAsync(
            new TournamentDrawDraft(TournamentId, OrganizationId, SampleRequest(), ScoringRules.Default, SampleDraw()),
            new TournamentDrawVersion(DrawId, 1)),
        "status" => repository.SetStatusAsync(new TournamentDrawVersion(DrawId, 1), TournamentDrawStatus.Published),
        "delete" => repository.DeleteDraftAsync(new TournamentDrawVersion(DrawId, 1)),
        _ => repository.UpsertResultAsync(new TournamentDrawVersion(DrawId, 1), SampleResult(), null),
    };

    /// <summary>A PostgREST error body (a function's <c>RAISE ... USING ERRCODE</c> keeps its message).</summary>
    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string message) => new(status)
    {
        Content = new StringContent(
            new JsonObject { ["code"] = code, ["message"] = message, ["details"] = null, ["hint"] = null }.ToJsonString(),
            Encoding.UTF8,
            "application/json"),
    };

    private static Draw SampleDraw() => Generate(SingleElimination(
        Participants(4, i => i),
        RandomizationPolicy.Seeded(18_000_000_000_000_000_001),
        thirdPlaceMatch: true,
        participationFormat: ParticipationFormat.Pair));

    private static DrawRequest SampleRequest() => new(
        DrawFormat.SingleElimination,
        ParticipationFormat.Pair,
        Participants(4, i => i),
        new RandomizationPolicy(RandomizationMode.Seeded, null),
        new DrawOptions(Knockout: new KnockoutOptions(true)));

    private static MatchResult SampleResult() => new(
        "KO-R1-M1",
        MatchStatus.Completed,
        [new SetScore(7, 6, new TiebreakScore(7, 4)), new SetScore(3, 6), new SetScore(10, 8)],
        MatchSide.Home,
        "Central",
        new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 31, 9, 5, 0, DateTimeKind.Utc),
        new DateTime(2026, 10, 31, 10, 35, 0, DateTimeKind.Utc));

    private static void AssertSampleResult(TournamentDrawMatchResult stored, Guid? courtId, DateTime? updatedAtUtc = null)
    {
        var expected = SampleResult();
        var result = stored.Result;
        Assert.Equal((expected.MatchId, expected.Status, expected.WinnerSide, expected.CourtName), (result.MatchId, result.Status, result.WinnerSide, result.CourtName));
        Assert.Equal(expected.Sets, result.Sets);
        AssertUtc(expected.ScheduledAtUtc!.Value, result.ScheduledAtUtc!.Value);
        AssertUtc(expected.StartedAtUtc!.Value, result.StartedAtUtc!.Value);
        AssertUtc(expected.FinishedAtUtc!.Value, result.FinishedAtUtc!.Value);
        Assert.Equal(courtId, stored.CourtId);
        AssertUtc(updatedAtUtc ?? new DateTime(2026, 10, 31, 10, 36, 0, DateTimeKind.Utc), stored.UpdatedAtUtc);
    }

    private static void AssertUtc(DateTime expected, DateTime actual)
    {
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
        Assert.Equal(expected, actual);
    }

    /// <summary>A stored draw row as PostgREST returns it (offset timestamps), optionally with embedded results.</summary>
    private static string DrawRowJson(
        Draw draw,
        string status = "published",
        string? publishedAt = "2026-10-30T09:00:00+00:00",
        string? results = null,
        int revision = 6)
    {
        var row = new JsonObject
        {
            ["id"] = DrawId.ToString(),
            ["tournament_id"] = TournamentId.ToString(),
            ["organization_id"] = OrganizationId.ToString(),
            ["status"] = status,
            ["format"] = "singleElimination",
            ["participation_format"] = "pair",
            ["randomization_mode"] = "seeded",
            ["seed"] = draw.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["options"] = JsonNode.Parse(TournamentDrawJson.WriteOptions(SampleRequest().Options, new ScoringRules(3, false)).GetRawText()),
            ["request"] = JsonNode.Parse(TournamentDrawJson.WriteRequest(SampleRequest(), new ScoringRules(3, false)).GetRawText()),
            ["structure"] = JsonNode.Parse(TournamentDrawJson.WriteDraw(draw).GetRawText()),
            ["published_at_utc"] = publishedAt,
            ["created_at_utc"] = "2026-10-29T08:00:00+00:00",
            ["updated_at_utc"] = "2026-10-30T09:00:00+00:00",
            ["revision"] = revision,
        };
        if (results is not null)
            row["results"] = JsonNode.Parse(results);

        return row.ToJsonString();
    }

    private static string ResultRowJson(bool withCourtId = true) =>
        $$$"""
        {
          "draw_id": "{{{DrawId}}}",
          "match_id": "KO-R1-M1",
          "status": "completed",
          "sets": [{"home":7,"away":6,"tiebreak":{"home":7,"away":4}},{"home":3,"away":6,"tiebreak":null},{"home":10,"away":8,"tiebreak":null}],
          "winner_side": "home",
          {{{(withCourtId ? $"\"court_id\": \"{CourtId}\"," : string.Empty)}}}
          "court_name": "Central",
          "scheduled_at_utc": "2026-10-31T09:00:00+00:00",
          "started_at_utc": "2026-10-31T06:05:00-03:00",
          "finished_at_utc": "2026-10-31T10:35:00+00:00",
          "created_at_utc": "2026-10-31T09:00:00+00:00",
          "updated_at_utc": "2026-10-31T10:36:00+00:00"
        }
        """;

    /// <summary>
    /// Echoes the save function the way it answers: the draw row (<c>to_jsonb</c>) built from the sent parameters, a
    /// draft without publication time, plus the generated columns.
    /// </summary>
    private static HttpResponseMessage EchoSavedDraw(HttpRequestMessage request, int revision)
    {
        var sent = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
        var row = new JsonObject { ["id"] = DrawId.ToString(), ["status"] = "draft", ["published_at_utc"] = null };
        foreach (var column in new[] { "tournament_id", "organization_id", "format", "participation_format", "randomization_mode", "seed", "options", "request", "structure" })
            row[column] = sent[$"p_{column}"]!.DeepClone();

        row["created_at_utc"] = "2026-10-29T08:00:00+00:00";
        row["updated_at_utc"] = sent["p_now_utc"]!.DeepClone();
        row["revision"] = revision;
        return Json(row.ToJsonString());
    }

    /// <summary>Echoes the result function: <c>{ revision, result }</c> with the row built from the sent parameters.</summary>
    private static HttpResponseMessage EchoResult(HttpRequestMessage request, int revision)
    {
        var sent = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
        var row = new JsonObject();
        foreach (var column in new[] { "draw_id", "match_id", "status", "sets", "winner_side", "court_id", "court_name", "scheduled_at_utc", "started_at_utc", "finished_at_utc" })
            row[column] = sent[$"p_{column}"]?.DeepClone();

        row["created_at_utc"] = "2026-10-31T09:00:00+00:00";
        row["updated_at_utc"] = sent["p_now_utc"]!.DeepClone();
        return Json(new JsonObject { ["revision"] = revision, ["result"] = row }.ToJsonString());
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed record Recorded(string Method, string Path, IReadOnlyList<string> Query, string? Prefer, string? Body);

    private static TournamentDrawRepository CreateRepository(
        List<Recorded> requests,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new RecordingHandler(async request =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            var uri = request.RequestUri!;
            var query = Uri.UnescapeDataString(uri.Query.TrimStart('?')).Split('&', StringSplitOptions.RemoveEmptyEntries);
            var prefer = request.Headers.TryGetValues("Prefer", out var values) ? string.Join(",", values) : null;
            requests.Add(new Recorded(request.Method.Method, uri.AbsolutePath, query, prefer, body));
            return respond(request);
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        return new TournamentDrawRepository(new ClientFactory(client), new TestClock(Now));
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
