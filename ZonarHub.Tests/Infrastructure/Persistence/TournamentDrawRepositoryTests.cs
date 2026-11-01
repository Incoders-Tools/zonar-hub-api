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
        Assert.Empty(await repository.ListResultsAsync(DrawId));
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
    public async Task SaveDraft_UpsertsOnTheTournament_WithSnakeCaseColumnsAndVersionedDocuments()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, EchoInsertedDraw);
        var draw = SampleDraw();
        var request = SampleRequest();
        var scoring = new ScoringRules(3, thirdSetSuperTiebreak: false);

        var saved = await repository.SaveDraftAsync(new TournamentDrawDraft(TournamentId, OrganizationId, request, scoring, draw));

        var sent = Assert.Single(requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/rest/v1/tournament_draws", sent.Path);
        Assert.Equal(["on_conflict=tournament_id", "select=*"], sent.Query);
        Assert.Equal("resolution=merge-duplicates,return=representation", sent.Prefer);

        var body = JsonNode.Parse(sent.Body!)!.AsObject();
        Assert.Equal(
            ["tournament_id", "organization_id", "status", "format", "participation_format", "randomization_mode", "seed",
                "options", "request", "structure", "published_at_utc", "updated_at_utc"],
            body.Select(property => property.Key));
        Assert.Equal(TournamentId.ToString(), body["tournament_id"]!.GetValue<string>());
        Assert.Equal(OrganizationId.ToString(), body["organization_id"]!.GetValue<string>());
        Assert.Equal("draft", body["status"]!.GetValue<string>());
        Assert.Equal("singleElimination", body["format"]!.GetValue<string>());
        Assert.Equal("pair", body["participation_format"]!.GetValue<string>());
        Assert.Equal("seeded", body["randomization_mode"]!.GetValue<string>());
        Assert.Equal(draw.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), body["seed"]!.GetValue<string>());
        Assert.Equal(TournamentDrawJson.WriteOptions(request.Options, scoring).GetRawText(), body["options"]!.ToJsonString());
        Assert.Equal(TournamentDrawJson.WriteRequest(request, scoring).GetRawText(), body["request"]!.ToJsonString());
        Assert.Equal(TournamentDrawJson.WriteDraw(draw).GetRawText(), body["structure"]!.ToJsonString());
        Assert.Null(body["published_at_utc"]);
        AssertUtc(Now, body["updated_at_utc"]!.GetValue<DateTime>().ToUniversalTime());

        Assert.Equal((DrawId, TournamentDrawStatus.Draft), (saved.Id, saved.Status));
        Assert.Equal(Snapshot(draw), Snapshot(saved.Structure));
        Assert.False(saved.Scoring.ThirdSetSuperTiebreak);
        Assert.Null(saved.PublishedAtUtc);
        AssertUtc(Now, saved.UpdatedAtUtc);
    }

    [Fact]
    public async Task SetStatus_Publish_SetsPublishedAtFromTheClock()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, request => EchoPatchedDraw(request, SampleDraw()));

        var published = await repository.SetStatusAsync(DrawId, TournamentDrawStatus.Published);

        var sent = Assert.Single(requests);
        Assert.Equal("PATCH", sent.Method);
        Assert.Equal("/rest/v1/tournament_draws", sent.Path);
        Assert.Equal([$"id=eq.{DrawId}", "select=*"], sent.Query);
        Assert.Equal("return=representation", sent.Prefer);
        var body = JsonNode.Parse(sent.Body!)!.AsObject();
        Assert.Equal(["status", "published_at_utc", "updated_at_utc"], body.Select(property => property.Key));
        Assert.Equal("published", body["status"]!.GetValue<string>());
        AssertUtc(Now, body["published_at_utc"]!.GetValue<DateTime>().ToUniversalTime());
        AssertUtc(Now, body["updated_at_utc"]!.GetValue<DateTime>().ToUniversalTime());

        Assert.Equal(TournamentDrawStatus.Published, published!.Status);
        AssertUtc(Now, published.PublishedAtUtc!.Value);
    }

    [Fact]
    public async Task SetStatus_Unpublish_ClearsPublishedAt()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, request => EchoPatchedDraw(request, SampleDraw()));

        var draft = await repository.SetStatusAsync(DrawId, TournamentDrawStatus.Draft);

        var body = JsonNode.Parse(Assert.Single(requests).Body!)!.AsObject();
        Assert.Equal("draft", body["status"]!.GetValue<string>());
        Assert.True(body.ContainsKey("published_at_utc"));
        Assert.Null(body["published_at_utc"]);
        Assert.Equal(TournamentDrawStatus.Draft, draft!.Status);
        Assert.Null(draft.PublishedAtUtc);
    }

    [Fact]
    public async Task SetStatus_UnknownDraw_ReturnsNull()
    {
        var repository = CreateRepository([], _ => Json("[]"));

        Assert.Null(await repository.SetStatusAsync(DrawId, TournamentDrawStatus.Published));
    }

    [Theory]
    [InlineData("[{\"id\":\"00000000-0000-0000-0000-0000000000d1\"}]", true)]
    [InlineData("[]", false)]
    public async Task DeleteDraft_DeletesOnlyADraft_AndReportsWhetherOneWasDeleted(string response, bool expected)
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, _ => Json(response));

        var deleted = await repository.DeleteDraftAsync(DrawId);

        Assert.Equal(expected, deleted);
        var sent = Assert.Single(requests);
        Assert.Equal("DELETE", sent.Method);
        Assert.Equal("/rest/v1/tournament_draws", sent.Path);
        Assert.Equal([$"id=eq.{DrawId}", "status=eq.draft", "select=id"], sent.Query);
        Assert.Equal("return=representation", sent.Prefer);
    }

    [Fact]
    public async Task UpsertResult_IsOneUpsertOnDrawAndMatch_WithSnakeCaseColumns()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, EchoResult);
        var result = SampleResult();

        var stored = await repository.UpsertResultAsync(DrawId, result, CourtId);

        var sent = Assert.Single(requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/rest/v1/tournament_draw_match_results", sent.Path);
        Assert.Equal(["on_conflict=draw_id,match_id", "select=*"], sent.Query);
        Assert.Equal("resolution=merge-duplicates,return=representation", sent.Prefer);
        var body = JsonNode.Parse(sent.Body!)!.AsObject();
        Assert.Equal(
            ["draw_id", "match_id", "status", "sets", "winner_side", "court_id", "court_name", "scheduled_at_utc",
                "started_at_utc", "finished_at_utc", "updated_at_utc"],
            body.Select(property => property.Key));
        Assert.Equal(DrawId.ToString(), body["draw_id"]!.GetValue<string>());
        Assert.Equal("KO-R1-M1", body["match_id"]!.GetValue<string>());
        Assert.Equal("completed", body["status"]!.GetValue<string>());
        Assert.Equal(
            """[{"home":7,"away":6,"tiebreak":{"home":7,"away":4}},{"home":3,"away":6,"tiebreak":null},{"home":10,"away":8,"tiebreak":null}]""",
            body["sets"]!.ToJsonString());
        Assert.Equal("home", body["winner_side"]!.GetValue<string>());
        Assert.Equal(CourtId.ToString(), body["court_id"]!.GetValue<string>());
        Assert.Equal("Central", body["court_name"]!.GetValue<string>());
        AssertUtc(new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc), body["scheduled_at_utc"]!.GetValue<DateTime>().ToUniversalTime());
        AssertUtc(new DateTime(2026, 10, 31, 9, 5, 0, DateTimeKind.Utc), body["started_at_utc"]!.GetValue<DateTime>().ToUniversalTime());
        AssertUtc(new DateTime(2026, 10, 31, 10, 35, 0, DateTimeKind.Utc), body["finished_at_utc"]!.GetValue<DateTime>().ToUniversalTime());
        AssertUtc(Now, body["updated_at_utc"]!.GetValue<DateTime>().ToUniversalTime());

        AssertSampleResult(stored, CourtId, Now);
    }

    [Fact]
    public async Task UpsertResult_ScheduledWithoutDetails_SendsNullsAndAnEmptySetList()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, EchoResult);

        var stored = await repository.UpsertResultAsync(DrawId, new MatchResult("RR-R1-M1", MatchStatus.Scheduled), courtId: null);

        var body = JsonNode.Parse(Assert.Single(requests).Body!)!.AsObject();
        Assert.Equal("scheduled", body["status"]!.GetValue<string>());
        Assert.Equal("[]", body["sets"]!.ToJsonString());
        foreach (var column in new[] { "winner_side", "court_id", "court_name", "scheduled_at_utc", "started_at_utc", "finished_at_utc" })
        {
            Assert.True(body.ContainsKey(column), column);
            Assert.Null(body[column]);
        }

        Assert.Equal((MatchStatus.Scheduled, null, null), (stored.Result.Status, stored.Result.WinnerSide, stored.CourtId));
        Assert.Empty(stored.Result.Sets);
    }

    [Fact]
    public async Task UpsertResult_LocalTimesAreConverted_AndUnspecifiedTimesAreTakenAsUtc()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, EchoResult);
        var scheduled = new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc);
        var result = new MatchResult(
            "RR-R1-M1",
            MatchStatus.Scheduled,
            scheduledAtUtc: scheduled.ToLocalTime(),
            startedAtUtc: new DateTime(2026, 10, 31, 9, 5, 0, DateTimeKind.Unspecified));

        var stored = await repository.UpsertResultAsync(DrawId, result, courtId: null);

        var body = JsonNode.Parse(Assert.Single(requests).Body!)!.AsObject();
        Assert.Equal("2026-10-31T09:00:00Z", body["scheduled_at_utc"]!.GetValue<string>());
        Assert.Equal("2026-10-31T09:05:00Z", body["started_at_utc"]!.GetValue<string>());
        AssertUtc(scheduled, stored.Result.ScheduledAtUtc!.Value);
        AssertUtc(new DateTime(2026, 10, 31, 9, 5, 0, DateTimeKind.Utc), stored.Result.StartedAtUtc!.Value);
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
    public async Task ListResults_FiltersByDraw_OrderedByMatch()
    {
        var requests = new List<Recorded>();
        var repository = CreateRepository(requests, _ => Json($"[{ResultRowJson()}]"));

        var results = await repository.ListResultsAsync(DrawId);

        var sent = Assert.Single(requests);
        Assert.Equal("GET", sent.Method);
        Assert.Equal("/rest/v1/tournament_draw_match_results", sent.Path);
        Assert.Equal(["select=*", $"draw_id=eq.{DrawId}", "order=match_id.asc"], sent.Query);
        AssertSampleResult(Assert.Single(results), CourtId);
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
        var repository = CreateRepository([], _ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("""{"code":"23503","message":"insert or update violates foreign key constraint"}"""),
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => repository.GetByTournamentIdAsync(TournamentId));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.SaveDraftAsync(
            new TournamentDrawDraft(TournamentId, OrganizationId, SampleRequest(), ScoringRules.Default, SampleDraw())));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.SetStatusAsync(DrawId, TournamentDrawStatus.Published));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.DeleteDraftAsync(DrawId));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.UpsertResultAsync(DrawId, SampleResult(), null));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.ListResultsAsync(DrawId));
        await Assert.ThrowsAsync<HttpRequestException>(() => repository.GetPublishedByTournamentIdAsync(TournamentId));
    }

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
    private static string DrawRowJson(Draw draw, string status = "published", string? publishedAt = "2026-10-30T09:00:00+00:00", string? results = null)
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

    /// <summary>Echoes an upserted draw the way PostgREST returns it: the sent columns plus the generated ones.</summary>
    private static HttpResponseMessage EchoInsertedDraw(HttpRequestMessage request)
    {
        var row = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
        row["id"] = DrawId.ToString();
        row["created_at_utc"] = "2026-10-29T08:00:00+00:00";
        return Json(new JsonArray(row).ToJsonString());
    }

    private static HttpResponseMessage EchoPatchedDraw(HttpRequestMessage request, Draw draw)
    {
        var row = JsonNode.Parse(DrawRowJson(draw))!.AsObject();
        foreach (var (column, value) in JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject())
            row[column] = value?.DeepClone();

        return Json(new JsonArray(row).ToJsonString());
    }

    private static HttpResponseMessage EchoResult(HttpRequestMessage request)
    {
        var row = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
        row["created_at_utc"] = "2026-10-31T09:00:00+00:00";
        return Json(new JsonArray(row).ToJsonString());
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
