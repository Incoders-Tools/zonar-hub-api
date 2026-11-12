using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.EligibilityProfiles;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.EligibilityProfiles;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Registrations;
using ZonarHub.Tests.Application.Registrations;
using static ZonarHub.Tests.Api.Registrations.AdminPlayersEndpointTests;
using static ZonarHub.Tests.Application.Registrations.RegistrationsTestHarness;

namespace ZonarHub.Tests.Api.Registrations;

/// <summary>Organization eligibility profiles (D1, D5) through the real handlers.</summary>
public class AdminEligibilityProfilesEndpointTests
{
    public static TheoryData<string> Operations => new() { "list", "get", "create", "update", "delete" };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task EveryEndpoint_AnotherTenantsOrganization_IsForbiddenWithoutWriting(string operation)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.ForeignOrganizationAsync();
        var profile = Seed(h, org, "Mixto 4ª");

        var result = await InvokeAsync(operation, h, org, profile.Id.Value);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal(AdminTournamentErrors.Forbidden.Code, problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(h.Profiles.Writes);
        Assert.NotNull(h.Profiles.Find(profile.Id));
    }

    [Fact]
    public async Task Create_StoresTheProfileWithItsSlotsInOrder()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();

        var result = await AdminEligibilityProfilesEndpointsExtensions.CreateAsync(org, Body(), h.Sender, CancellationToken.None);

        var created = Assert.IsType<Created<EligibilityProfileResponse>>(result);
        var profile = created.Value!;
        Assert.Equal($"/api/admin/organizations/{org}/eligibility-profiles/{profile.Id}", created.Location);
        Assert.Equal(("Mixto 4ª", "Una dama y un caballero", true), (profile.Name, profile.Description, profile.IsActive));
        Assert.Equal(
            [new EligibilitySlotBody(1, Male, Fourth, 18, null, "Drive", true), new EligibilitySlotBody(2, Female, Fourth, null, null, null, false)],
            profile.Slots);
        Assert.Equal((Now, Now), (profile.CreatedAt, profile.UpdatedAt));
        Assert.Equal([$"save:{profile.Id}"], h.Profiles.Writes);
    }

    [Theory]
    [InlineData("name", 400, "eligibility_profiles.name_required")]
    [InlineData("slots", 400, "eligibility_profiles.slots_required")]
    [InlineData("ages", 400, "eligibility_profiles.invalid_age_range")]
    [InlineData("gender", 400, "eligibility_profiles.unknown_gender")]
    [InlineData("category", 400, "eligibility_profiles.unknown_category")]
    [InlineData("taken", 409, "eligibility_profiles.name_taken")]
    public async Task Create_Refused_WritesNothing(string scenario, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        Seed(h, org, "MIXTO 4ª");
        var body = scenario switch
        {
            "name" => Body() with { Name = "" },
            "slots" => Body() with { Slots = [] },
            "ages" => Body() with { Slots = [new EligibilitySlotBody(1, null, null, 40, 30, null)] },
            "gender" => Body() with { Slots = [new EligibilitySlotBody(1, Guid.NewGuid(), null, null, null, null)] },
            "category" => Body() with { Slots = [new EligibilitySlotBody(1, null, Guid.NewGuid(), null, null, null)] },
            _ => Body(),
        };

        var result = await AdminEligibilityProfilesEndpointsExtensions.CreateAsync(org, body, h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Profiles.Writes);
    }

    [Fact]
    public async Task ListAndGet_ReturnOnlyTheOrganizationsProfiles()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var b = Seed(h, org, "B");
        Seed(h, org, "A");
        var foreign = Seed(h, Guid.NewGuid(), "C");

        var list = Assert.IsType<Ok<IReadOnlyList<EligibilityProfileResponse>>>(
            await AdminEligibilityProfilesEndpointsExtensions.ListAsync(org, h.Sender, CancellationToken.None)).Value!;
        var got = Assert.IsType<Ok<EligibilityProfileResponse>>(
            await AdminEligibilityProfilesEndpointsExtensions.GetAsync(org, b.Id.Value, h.Sender, CancellationToken.None)).Value!;

        Assert.Equal(["A", "B"], list.Select(p => p.Name));
        Assert.Equal(b.Id.Value, got.Id);
        AssertProblem(await AdminEligibilityProfilesEndpointsExtensions.GetAsync(org, foreign.Id.Value, h.Sender, CancellationToken.None),
            StatusCodes.Status404NotFound, "eligibility_profiles.not_found");
    }

    [Fact]
    public async Task Update_ReplacesNameActivityAndSlots()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var profile = Seed(h, org, "Old");
        h.Tournaments.Users.Clock.UtcNow = Now.AddHours(1);

        var result = await AdminEligibilityProfilesEndpointsExtensions.UpdateAsync(
            org, profile.Id.Value, Body() with { IsActive = false, Slots = [new EligibilitySlotBody(1, null, Third, null, null, null)] },
            h.Sender, CancellationToken.None);

        var updated = Assert.IsType<Ok<EligibilityProfileResponse>>(result).Value!;
        Assert.Equal(("Mixto 4ª", false), (updated.Name, updated.IsActive));
        Assert.Equal([new EligibilitySlotBody(1, null, Third, null, null, null, true)], updated.Slots);
        Assert.Equal(Now.AddHours(1), updated.UpdatedAt);
        Assert.Equal("Mixto 4ª", h.Profiles.Find(profile.Id)!.Name);
    }

    [Theory]
    [InlineData("unknown", 404, "eligibility_profiles.not_found")]
    [InlineData("invalid", 400, "eligibility_profiles.name_required")]
    [InlineData("taken", 409, "eligibility_profiles.name_taken")]
    public async Task Update_Refused_LeavesTheProfileUnchanged(string scenario, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var profile = Seed(h, org, "Old");
        Seed(h, org, "Mixto 4ª");
        var (id, body) = scenario switch
        {
            "unknown" => (Guid.NewGuid(), Body()),
            "invalid" => (profile.Id.Value, Body() with { Name = " " }),
            _ => (profile.Id.Value, Body()),
        };

        var result = await AdminEligibilityProfilesEndpointsExtensions.UpdateAsync(org, id, body, h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Profiles.Writes);
        Assert.Equal("Old", h.Profiles.Find(profile.Id)!.Name);
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedProfile()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var profile = Seed(h, org, "Old");

        var result = await AdminEligibilityProfilesEndpointsExtensions.DeleteAsync(org, profile.Id.Value, h.Sender, CancellationToken.None);

        Assert.IsType<NoContent>(result);
        Assert.Null(h.Profiles.Find(profile.Id));
    }

    [Theory]
    [InlineData("unknown", 404, "eligibility_profiles.not_found")]
    [InlineData("in_use", 409, "eligibility_profiles.in_use")]
    public async Task Delete_Refused_KeepsTheProfile(string scenario, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var profile = Seed(h, org, "Old");
        h.Profiles.TournamentProfiles[Guid.NewGuid()] = profile.Id;
        var id = scenario == "unknown" ? Guid.NewGuid() : profile.Id.Value;

        var result = await AdminEligibilityProfilesEndpointsExtensions.DeleteAsync(org, id, h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Profiles.Writes);
        Assert.NotNull(h.Profiles.Find(profile.Id));
    }

    internal static EligibilityProfile Seed(RegistrationsTestHarness h, Guid org, string name, params EligibilitySlot[] slots)
    {
        var profile = EligibilityProfile.Reconstitute(
            EligibilityProfileId.New(), new OrganizationId(org), name, null, true,
            slots.Length > 0 ? slots : [new EligibilitySlot(1, null, null, null, null, null, true)], Now, Now);
        h.Profiles.Seed(profile);
        return profile;
    }

    private static EligibilityProfileBody Body() => new(
        " Mixto 4ª ",
        "Una dama y un caballero",
        [new EligibilitySlotBody(2, Female, Fourth, null, null, " ", false), new EligibilitySlotBody(1, Male, Fourth, 18, null, "Drive")]);

    private static Task<IResult> InvokeAsync(string operation, RegistrationsTestHarness h, Guid org, Guid id) => operation switch
    {
        "list" => AdminEligibilityProfilesEndpointsExtensions.ListAsync(org, h.Sender, CancellationToken.None),
        "get" => AdminEligibilityProfilesEndpointsExtensions.GetAsync(org, id, h.Sender, CancellationToken.None),
        "create" => AdminEligibilityProfilesEndpointsExtensions.CreateAsync(org, Body(), h.Sender, CancellationToken.None),
        "update" => AdminEligibilityProfilesEndpointsExtensions.UpdateAsync(org, id, Body(), h.Sender, CancellationToken.None),
        _ => AdminEligibilityProfilesEndpointsExtensions.DeleteAsync(org, id, h.Sender, CancellationToken.None),
    };
}
