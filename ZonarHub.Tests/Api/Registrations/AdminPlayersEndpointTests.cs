using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Players;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.Players;
using ZonarHub.Domain.Players;
using ZonarHub.Tests.Application.Registrations;
using static ZonarHub.Tests.Application.Registrations.RegistrationsTestHarness;

namespace ZonarHub.Tests.Api.Registrations;

/// <summary>
/// Organization players (S3, D5) through the real handlers: tenant rule, search, create and update with the domain and
/// catalog validation and the store's unique email and document.
/// </summary>
public class AdminPlayersEndpointTests
{
    public static TheoryData<string> Operations => new() { "list", "get", "create", "update" };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryEndpoint_AnotherTenantsOrganization_IsForbiddenWithoutWriting(string operation)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.ForeignOrganizationAsync();
        var player = h.SeedPlayer(org, "Ana", "Gil");

        var result = await InvokeAsync(operation, h, org, player.Id.Value);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(h.Players.Writes);
        Assert.Equal("Gil", h.Players.Find(player.Id)!.LastName);
    }

    [Fact]
    public async Task List_SearchesTheOrganizationsPlayersByNameEmailOrDocument()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        h.SeedPlayer(org, "Ana", "Gil", email: "ana@club.test");
        h.SeedPlayer(org, "Bea", "Paz");
        h.SeedPlayer(org, "Carla", "Gilardi", isActive: false);
        h.SeedPlayer(Guid.NewGuid(), "Ana", "Gil");

        var all = Page(await AdminPlayersEndpointsExtensions.ListAsync(org, null, null, null, null, h.Sender, CancellationToken.None));
        var gil = Page(await AdminPlayersEndpointsExtensions.ListAsync(org, "gil", null, null, null, h.Sender, CancellationToken.None));
        var active = Page(await AdminPlayersEndpointsExtensions.ListAsync(org, "GIL", true, null, null, h.Sender, CancellationToken.None));
        var byEmail = Page(await AdminPlayersEndpointsExtensions.ListAsync(org, "club.test", null, null, null, h.Sender, CancellationToken.None));
        var paged = Page(await AdminPlayersEndpointsExtensions.ListAsync(org, null, null, 2, 2, h.Sender, CancellationToken.None));

        Assert.Equal(["Gil", "Gilardi", "Paz"], all.Items.Select(p => p.LastName));
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(["Gil", "Gilardi"], gil.Items.Select(p => p.LastName));
        Assert.Equal(["Ana Gil"], active.Items.Select(p => p.DisplayName));
        Assert.Equal(["ana@club.test"], byEmail.Items.Select(p => p.Email));
        Assert.Equal(["Paz"], paged.Items.Select(p => p.LastName));
        Assert.Equal((2, 2, 3), (paged.Page, paged.PageSize, paged.TotalCount));
    }

    [Fact]
    public async Task Create_StoresANormalizedActivePlayer()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();

        var result = await AdminPlayersEndpointsExtensions.CreateAsync(
            org, Body(email: "  Ana@Club.TEST ", club: " Norte "), h.Sender, CancellationToken.None);

        var created = Assert.IsType<Created<PlayerResponse>>(result);
        var player = created.Value!;
        Assert.Equal($"/api/admin/organizations/{org}/players/{player.Id}", created.Location);
        Assert.Equal(("Ana", "Gil", "Ana Gil", "ana@club.test", "Norte"), (player.FirstName, player.LastName, player.DisplayName, player.Email, player.Club));
        Assert.Equal((org, Female, Fourth, true), (player.OrganizationId, player.GenderId, player.CategoryId, player.IsActive));
        Assert.Equal(new DateOnly(1990, 5, 1), player.BirthDate);
        Assert.Equal((Now, Now), (player.CreatedAt, player.UpdatedAt));
        Assert.Equal("Ana", h.Players.Find(new PlayerId(player.Id))!.FirstName);
    }

    [Theory]
    [InlineData("firstName", "players.first_name_required")]
    [InlineData("email", "players.invalid_email")]
    [InlineData("birthDate", "players.birth_date_in_future")]
    [InlineData("gender", "players.unknown_gender")]
    [InlineData("category", "players.unknown_category")]
    public async Task Create_InvalidInput_IsBadRequestWithoutWriting(string field, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var body = field switch
        {
            "firstName" => Body() with { FirstName = " " },
            "email" => Body(email: "ana-at-club"),
            "birthDate" => Body() with { BirthDate = new DateOnly(2026, 11, 12) },
            "gender" => Body() with { GenderId = Guid.NewGuid() },
            _ => Body() with { CategoryId = Guid.NewGuid() },
        };

        var result = await AdminPlayersEndpointsExtensions.CreateAsync(org, body, h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status400BadRequest, code);
        Assert.Empty(h.Players.Writes);
    }

    [Theory]
    [InlineData("email", "players.email_taken")]
    [InlineData("document", "players.document_taken")]
    public async Task Create_TakenEmailOrDocument_IsConflictWithoutWriting(string field, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        await AdminPlayersEndpointsExtensions.CreateAsync(org, Body(email: "ana@club.test") with { DocumentId = "30111222" }, h.Sender, CancellationToken.None);
        h.Players.Writes.Clear();
        var body = field == "email" ? Body(email: "ANA@club.test") : Body() with { DocumentId = "30111222" };

        var result = await AdminPlayersEndpointsExtensions.CreateAsync(org, body with { FirstName = "Other" }, h.Sender, CancellationToken.None);

        AssertProblem(result, StatusCodes.Status409Conflict, code);
        Assert.Empty(h.Players.Writes);
    }

    [Fact]
    public async Task Get_ReturnsThePlayerAndUnknownOrForeignIdsAreNotFound()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var player = h.SeedPlayer(org, "Ana", "Gil");
        var foreign = h.SeedPlayer(Guid.NewGuid(), "Bea", "Paz");

        var found = Assert.IsType<Ok<PlayerResponse>>(
            await AdminPlayersEndpointsExtensions.GetAsync(org, player.Id.Value, h.Sender, CancellationToken.None));

        Assert.Equal(player.Id.Value, found.Value!.Id);
        AssertProblem(await AdminPlayersEndpointsExtensions.GetAsync(org, Guid.NewGuid(), h.Sender, CancellationToken.None),
            StatusCodes.Status404NotFound, "players.not_found");
        AssertProblem(await AdminPlayersEndpointsExtensions.GetAsync(org, foreign.Id.Value, h.Sender, CancellationToken.None),
            StatusCodes.Status404NotFound, "players.not_found");
    }

    [Fact]
    public async Task Update_ReplacesTheDetailsAndActivity()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var player = h.SeedPlayer(org, "Ana", "Gil");
        h.Tournaments.Users.Clock.UtcNow = Now.AddHours(1);

        var result = await AdminPlayersEndpointsExtensions.UpdateAsync(
            org, player.Id.Value, Body(club: "Sur") with { LastName = "Gil Paz", IsActive = false }, h.Sender, CancellationToken.None);

        var updated = Assert.IsType<Ok<PlayerResponse>>(result).Value!;
        Assert.Equal(("Gil Paz", "Sur", false), (updated.LastName, updated.Club, updated.IsActive));
        Assert.Equal((Now, Now.AddHours(1)), (updated.CreatedAt, updated.UpdatedAt));
        Assert.Equal([$"update:{player.Id}"], h.Players.Writes);
        Assert.False(h.Players.Find(player.Id)!.IsActive);
    }

    [Theory]
    [InlineData("unknown", 404, "players.not_found")]
    [InlineData("invalid", 400, "players.last_name_required")]
    [InlineData("taken", 409, "players.email_taken")]
    public async Task Update_Refused_LeavesThePlayerUnchanged(string scenario, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var player = h.SeedPlayer(org, "Ana", "Gil");
        h.SeedPlayer(org, "Bea", "Paz", email: "bea@club.test");
        var (id, body) = scenario switch
        {
            "unknown" => (Guid.NewGuid(), Body()),
            "invalid" => (player.Id.Value, Body() with { LastName = null }),
            _ => (player.Id.Value, Body(email: "bea@club.test")),
        };

        var result = await AdminPlayersEndpointsExtensions.UpdateAsync(org, id, body, h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Players.Writes);
        Assert.Equal(("Gil", (string?)null), (h.Players.Find(player.Id)!.LastName, h.Players.Find(player.Id)!.Email));
    }

    private static PlayerBody Body(string? email = null, string? club = null) =>
        new("Ana", "Gil", email, "+54 11 5555", null, new DateOnly(1990, 5, 1), Female, Fourth, club);

    private static Task<IResult> InvokeAsync(string operation, RegistrationsTestHarness h, Guid org, Guid playerId) => operation switch
    {
        "list" => AdminPlayersEndpointsExtensions.ListAsync(org, null, null, null, null, h.Sender, CancellationToken.None),
        "get" => AdminPlayersEndpointsExtensions.GetAsync(org, playerId, h.Sender, CancellationToken.None),
        "create" => AdminPlayersEndpointsExtensions.CreateAsync(org, Body(), h.Sender, CancellationToken.None),
        _ => AdminPlayersEndpointsExtensions.UpdateAsync(org, playerId, Body() with { LastName = "Changed" }, h.Sender, CancellationToken.None),
    };

    private static PageResult<PlayerResponse> Page(IResult result) => Assert.IsType<Ok<PageResult<PlayerResponse>>>(result).Value!;

    internal static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }
}
