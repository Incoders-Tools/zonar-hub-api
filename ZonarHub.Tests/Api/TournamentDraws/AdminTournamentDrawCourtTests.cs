using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.TournamentDraws;
using ZonarHub.Application.Features.TournamentDraws.Courts;
using ZonarHub.Domain.Draws.Results;
using ZonarHub.Tests.Application.TournamentDraws;
using static ZonarHub.Tests.Application.TournamentDraws.TournamentDrawBodies;

namespace ZonarHub.Tests.Api.TournamentDraws;

/// <summary>
/// Courts on draw results (D13): a <c>courtId</c> must be one of the tournament's eligible courts (its complex's active
/// courts, or its selected courts when set, of its own organization) and stores the court's current name; free-text
/// names keep working; only the admin JSON carries court ids; the dialog lists the eligible courts.
/// </summary>
public class AdminTournamentDrawCourtTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RecordResult_CourtOfTheTournamentsComplex_StoresItsIdAndCurrentNameIgnoringTheSentName()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var complex = await h.SeedComplexAsync(tournament.OrganizationId);
        var central = await h.SeedCourtAsync(complex, "Central");
        await h.AtComplexAsync(tournament, complex);
        h.Draws.SeedDraft(tournament, Knockout(4));

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id,
            "KO-R1-M1",
            Completed() with { CourtId = central.Id.Value, CourtName = "Typed name" },
            h.Sender,
            CancellationToken.None));

        var stored = Assert.Single(h.Draws.UpsertedResults);
        Assert.Equal(central.Id.Value, stored.CourtId);
        Assert.Equal("Central", stored.Result.CourtName);
        var result = Match(draw, "KO-R1-M1").Result!;
        Assert.Equal(central.Id.Value, result.CourtId);
        Assert.Equal("Central", result.CourtName);
    }

    [Fact]
    public async Task RecordResult_SelectedCourt_IsAccepted()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var complex = await h.SeedComplexAsync(tournament.OrganizationId);
        await h.SeedCourtAsync(complex, "Central");
        var second = await h.SeedCourtAsync(complex, "Court 2");
        await h.AtComplexAsync(tournament, complex, second);
        h.Draws.SeedDraft(tournament, Knockout(4));

        Draw(await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, "KO-R1-M1", Completed() with { CourtId = second.Id.Value }, h.Sender, CancellationToken.None));

        var stored = Assert.Single(h.Draws.UpsertedResults);
        Assert.Equal((second.Id.Value, "Court 2"), (stored.CourtId!.Value, stored.Result.CourtName));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("otherComplex")]
    [InlineData("notSelected")]
    [InlineData("inactive")]
    [InlineData("otherTenant")]
    [InlineData("noComplex")]
    public async Task RecordResult_IneligibleCourt_IsInvalidCourtWithoutWriting(string scenario)
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var complex = await h.SeedComplexAsync(
            scenario == "otherTenant" ? Guid.NewGuid() : tournament.OrganizationId);
        var central = await h.SeedCourtAsync(complex, "Central");
        var second = await h.SeedCourtAsync(complex, "Court 2");
        var inactive = await h.SeedCourtAsync(complex, "Old court", isActive: false);
        var elsewhere = await h.SeedCourtAsync(await h.SeedComplexAsync(tournament.OrganizationId, "Club Sur"), "Sur 1");
        await (scenario switch
        {
            "notSelected" => h.AtComplexAsync(tournament, complex, second),
            "noComplex" => h.AtComplexAsync(tournament, null),
            _ => h.AtComplexAsync(tournament, complex),
        });
        h.Draws.SeedDraft(tournament, Knockout(4));
        var courtId = scenario switch
        {
            "unknown" => Guid.NewGuid(),
            "otherComplex" => elsewhere.Id.Value,
            "inactive" => inactive.Id.Value,
            _ => central.Id.Value,
        };

        var result = await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, "KO-R1-M1", Completed() with { CourtId = courtId }, h.Sender, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("draws.results.invalid_court", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("draws.errors.results.invalid_court", problem.ProblemDetails.Detail);
        Assert.Empty(h.Draws.Writes);
        Assert.Empty(h.Draws.UpsertedResults);
        Assert.Empty((await h.Draws.GetByTournamentIdAsync(tournament.Id))!.Results);
    }

    [Fact]
    public async Task RecordResult_FreeTextCourtNameWithoutCourtId_IsStoredAsTyped()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var complex = await h.SeedComplexAsync(tournament.OrganizationId);
        await h.SeedCourtAsync(complex, "Central");
        await h.AtComplexAsync(tournament, complex);
        h.Draws.SeedDraft(tournament, Knockout(4));

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, "KO-R1-M1", Completed() with { CourtName = " Pista prestada " }, h.Sender, CancellationToken.None));

        var stored = Assert.Single(h.Draws.UpsertedResults);
        Assert.Null(stored.CourtId);
        Assert.Equal("Pista prestada", stored.Result.CourtName);
        Assert.Null(Match(draw, "KO-R1-M1").Result!.CourtId);
    }

    [Fact]
    public async Task RecordResult_ClearingWithTheCourt_KeepsTheCourtAndSchedule()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var complex = await h.SeedComplexAsync(tournament.OrganizationId);
        var central = await h.SeedCourtAsync(complex, "Central");
        await h.AtComplexAsync(tournament, complex);
        h.Draws.SeedDraft(tournament, Knockout(4));
        h.Draws.SeedResult(
            tournament.Id,
            new MatchResult("KO-R1-M1", MatchStatus.Completed, [new SetScore(6, 4), new SetScore(6, 4)], MatchSide.Home, "Central"),
            central.Id.Value);
        var scheduledAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var clear = new MatchResultBody("scheduled", null, null, central.Id.Value, null, scheduledAt, null, null);

        var draw = Draw(await AdminTournamentDrawEndpointsExtensions.RecordResultAsync(
            tournament.Id, "KO-R1-M1", clear, h.Sender, CancellationToken.None));

        var cleared = Match(draw, "KO-R1-M1").Result!;
        Assert.Equal("scheduled", cleared.Status);
        Assert.Empty(cleared.Sets);
        Assert.Equal((central.Id.Value, "Central", scheduledAt.UtcDateTime), (cleared.CourtId!.Value, cleared.CourtName, cleared.ScheduledAt!.Value));
        Assert.Equal(central.Id.Value, Assert.Single(h.Draws.UpsertedResults).CourtId);
    }

    [Fact]
    public async Task AdminJson_ResultWithACourt_HasCourtIdAndAResultWithoutOneOmitsIt()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        h.Draws.SeedDraft(tournament, Knockout(4));
        var courtId = Guid.NewGuid();
        h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M1") with { CourtName = "Central" }, courtId);
        h.Draws.SeedResult(tournament.Id, HomeWin("KO-R1-M2") with { CourtName = "Prestada" });

        using var json = JsonDocument.Parse(Json(Draw(
            await AdminTournamentDrawEndpointsExtensions.GetAsync(tournament.Id, h.Sender, CancellationToken.None))));

        var matches = json.RootElement.GetProperty("phases")[0].GetProperty("rounds")[0].GetProperty("matches");
        var withCourt = matches[0].GetProperty("result");
        Assert.Equal(["status", "sets", "winnerSide", "courtId", "courtName", "scheduledAt", "startedAt", "finishedAt"], Names(withCourt));
        Assert.Equal(courtId.ToString(), withCourt.GetProperty("courtId").GetString());
        Assert.Equal("Central", withCourt.GetProperty("courtName").GetString());
        var withoutCourt = matches[1].GetProperty("result");
        Assert.Equal(["status", "sets", "winnerSide", "courtName", "scheduledAt", "startedAt", "finishedAt"], Names(withoutCourt));
        Assert.Equal("Prestada", withoutCourt.GetProperty("courtName").GetString());
    }

    [Fact]
    public async Task Mapper_CourtIds_AreOnlyWrittenForTheAdminAudience()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var record = h.Draws.SeedDraft(tournament, Knockout(4));
        var courtId = Guid.NewGuid();
        var resolved = DrawProgression.Resolve(record.Structure, [HomeWin("KO-R1-M1") with { CourtName = "Central" }], record.Scoring).Value;
        var courtIds = new Dictionary<string, Guid> { ["KO-R1-M1"] = courtId };

        var admin = Json(TournamentDrawResponseMapper.ToResponse(
            resolved, TournamentDrawStatus.Published, TournamentDrawsTestHarness.Now, record.Options, TournamentDrawAudience.Admin, courtIds));
        var published = Json(TournamentDrawResponseMapper.ToResponse(
            resolved, TournamentDrawStatus.Published, TournamentDrawsTestHarness.Now, record.Options, TournamentDrawAudience.Public, courtIds));

        Assert.Contains($"\"courtId\":\"{courtId}\"", admin, StringComparison.Ordinal);
        Assert.DoesNotContain("courtId", published, StringComparison.Ordinal);
        Assert.DoesNotContain(courtId.ToString(), published, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"courtName\":\"Central\"", published, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListCourts_WithoutSelection_ReturnsTheComplexsActiveCourtsByName()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var complex = await h.SeedComplexAsync(tournament.OrganizationId);
        var second = await h.SeedCourtAsync(complex, "Court 2");
        var central = await h.SeedCourtAsync(complex, "Central");
        await h.SeedCourtAsync(complex, "Old court", isActive: false);
        await h.SeedCourtAsync(await h.SeedComplexAsync(tournament.OrganizationId, "Club Sur"), "Sur 1");
        await h.AtComplexAsync(tournament, complex);

        var result = await AdminTournamentDrawEndpointsExtensions.ListCourtsAsync(tournament.Id, h.Sender, CancellationToken.None);

        var courts = Courts(result);
        Assert.Equal(
            [new TournamentDrawCourtResponse(central.Id.Value, "Central"), new TournamentDrawCourtResponse(second.Id.Value, "Court 2")],
            courts);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(courts, Web));
        Assert.Equal(["id", "name"], Names(json.RootElement[0]));
        Assert.Empty(h.Draws.Writes);
    }

    [Fact]
    public async Task ListCourts_WithSelection_ReturnsOnlyTheSelectedCourts()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var complex = await h.SeedComplexAsync(tournament.OrganizationId);
        await h.SeedCourtAsync(complex, "Central");
        var second = await h.SeedCourtAsync(complex, "Court 2");
        await h.AtComplexAsync(tournament, complex, second);

        var courts = Courts(await AdminTournamentDrawEndpointsExtensions.ListCourtsAsync(tournament.Id, h.Sender, CancellationToken.None));

        Assert.Equal([new TournamentDrawCourtResponse(second.Id.Value, "Court 2")], courts);
    }

    [Theory]
    [InlineData("noComplex")]
    [InlineData("unknownComplex")]
    [InlineData("otherTenantComplex")]
    public async Task ListCourts_TournamentWithoutAnOwnComplex_IsEmpty(string scenario)
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.OwnTournamentAsync();
        var complex = await h.SeedComplexAsync(
            scenario == "otherTenantComplex" ? Guid.NewGuid() : tournament.OrganizationId);
        var central = await h.SeedCourtAsync(complex, "Central");
        await (scenario switch
        {
            "noComplex" => h.AtComplexAsync(tournament, null),
            "unknownComplex" => h.Tournaments.Tournaments.UpdateAsync(tournament with { ComplexId = Guid.NewGuid() }),
            _ => h.AtComplexAsync(tournament, complex, central),
        });

        var courts = Courts(await AdminTournamentDrawEndpointsExtensions.ListCourtsAsync(tournament.Id, h.Sender, CancellationToken.None));

        Assert.Empty(courts);
    }

    [Fact]
    public async Task ListCourts_AnotherTenantsTournament_IsForbidden()
    {
        var h = new TournamentDrawsTestHarness();
        var tournament = await h.ForeignTournamentWithDrawAsync();
        var complex = await h.SeedComplexAsync(tournament.OrganizationId);
        await h.SeedCourtAsync(complex, "Central");
        await h.AtComplexAsync(tournament, complex);

        var result = await AdminTournamentDrawEndpointsExtensions.ListCourtsAsync(tournament.Id, h.Sender, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
    }

    private static MatchResult HomeWin(string matchId) =>
        new(matchId, MatchStatus.Completed, [new SetScore(6, 4), new SetScore(6, 4)], MatchSide.Home);

    private static TournamentDrawResponse Draw(IResult result) =>
        Assert.IsType<Ok<TournamentDrawResponse>>(result).Value!;

    private static IReadOnlyList<TournamentDrawCourtResponse> Courts(IResult result) =>
        Assert.IsType<Ok<IReadOnlyList<TournamentDrawCourtResponse>>>(result).Value!;

    private static DrawMatchResponse Match(TournamentDrawResponse draw, string matchId) =>
        draw.Phases.SelectMany(p => p.Rounds).SelectMany(r => r.Matches).Single(m => m.Id == matchId);

    private static string Json(TournamentDrawResponse draw) => JsonSerializer.Serialize(draw, Web);

    private static List<string> Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).ToList();
}
