using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.EligibilityProfiles;
using ZonarHub.ApiService.Endpoints.Admin.Circuit.Tournaments;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.EligibilityProfiles;
using ZonarHub.Domain.Registrations;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminTournaments;
using ZonarHub.Tests.Application.Registrations;
using static ZonarHub.Tests.Api.Registrations.AdminEligibilityProfilesEndpointTests;
using static ZonarHub.Tests.Api.Registrations.AdminPlayersEndpointTests;

namespace ZonarHub.Tests.Api.Registrations;

/// <summary>Assigning an eligibility profile to a tournament (R2b, D1, D3, L8) through the real handlers.</summary>
public class AdminTournamentEligibilityProfileEndpointTests
{
    [Fact]
    public async Task Get_TournamentWithoutProfile_ReturnsNull()
    {
        var h = new RegistrationsTestHarness();
        var tournament = await h.TournamentAsync(await h.OwnOrganizationAsync());

        var result = await AdminEligibilityProfilesEndpointsExtensions.GetTournamentProfileAsync(tournament.Id, h.Sender, CancellationToken.None);

        Assert.Null(Assert.IsType<Ok<TournamentEligibilityProfileBody>>(result).Value!.EligibilityProfileId);
    }

    [Fact]
    public async Task SetGetClear_RoundTrip()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var profile = Seed(h, org, "Mixto 4ª");

        var set = await AdminEligibilityProfilesEndpointsExtensions.SetTournamentProfileAsync(
            tournament.Id, new TournamentEligibilityProfileBody(profile.Id.Value), h.Sender, CancellationToken.None);
        var got = await AdminEligibilityProfilesEndpointsExtensions.GetTournamentProfileAsync(tournament.Id, h.Sender, CancellationToken.None);

        Assert.Equal(profile.Id.Value, Assert.IsType<Ok<TournamentEligibilityProfileBody>>(set).Value!.EligibilityProfileId);
        Assert.Equal(profile.Id.Value, Assert.IsType<Ok<TournamentEligibilityProfileBody>>(got).Value!.EligibilityProfileId);
        Assert.Equal(profile.Id, h.Profiles.TournamentProfiles[tournament.Id]);

        var cleared = await AdminEligibilityProfilesEndpointsExtensions.SetTournamentProfileAsync(
            tournament.Id, new TournamentEligibilityProfileBody(null), h.Sender, CancellationToken.None);
        var afterClear = await AdminEligibilityProfilesEndpointsExtensions.GetTournamentProfileAsync(tournament.Id, h.Sender, CancellationToken.None);

        Assert.Null(Assert.IsType<Ok<TournamentEligibilityProfileBody>>(cleared).Value!.EligibilityProfileId);
        Assert.Null(Assert.IsType<Ok<TournamentEligibilityProfileBody>>(afterClear).Value!.EligibilityProfileId);
        Assert.False(h.Profiles.TournamentProfiles.ContainsKey(tournament.Id));
        Assert.Equal([$"tournament:{tournament.Id}:{profile.Id}", $"tournament:{tournament.Id}:none"], h.Profiles.Writes);
    }

    [Fact]
    public async Task Set_InactiveProfileOfTheOrganization_IsAssigned()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var inactive = Seed(h, org, "Retirado");
        inactive.Update(inactive.Name, null, false, inactive.Slots, RegistrationsTestHarness.Now);

        var result = await AdminEligibilityProfilesEndpointsExtensions.SetTournamentProfileAsync(
            tournament.Id, new TournamentEligibilityProfileBody(inactive.Id.Value), h.Sender, CancellationToken.None);

        Assert.False(h.Profiles.Find(inactive.Id)!.IsActive);
        Assert.Equal(inactive.Id.Value, Assert.IsType<Ok<TournamentEligibilityProfileBody>>(result).Value!.EligibilityProfileId);
        Assert.Equal(inactive.Id, h.Profiles.TournamentProfiles[tournament.Id]);
    }

    public static TheoryData<string, int, string> Refusals => new()
    {
        { "unknown tournament", StatusCodes.Status404NotFound, AdminTournamentErrors.NotFound.Code },
        { "foreign tenant", StatusCodes.Status403Forbidden, AdminTournamentErrors.Forbidden.Code },
        { "profile of a sibling organization", StatusCodes.Status404NotFound, EligibilityProfileErrors.NotFound.Code },
        { "profile of a foreign organization", StatusCodes.Status404NotFound, EligibilityProfileErrors.NotFound.Code },
        { "unknown profile", StatusCodes.Status404NotFound, EligibilityProfileErrors.NotFound.Code },
        { "profile deleted before the write", StatusCodes.Status404NotFound, EligibilityProfileErrors.NotFound.Code },
        { "tournament deleted before the write", StatusCodes.Status404NotFound, AdminTournamentErrors.NotFound.Code },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Set_Refused_LeavesTheStoredProfileUnchanged(string scenario, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var tenant = Guid.NewGuid();
        var org = (await h.Tournaments.Users.SeedOrganizationAsync(tenant, "Org A")).Id.Value;
        var sibling = (await h.Tournaments.Users.SeedOrganizationAsync(tenant, "Org B")).Id.Value;
        await AdminTournamentsItemAccessTests.AuthenticateAsync(h.Tournaments, UserRole.Admin, scenario == "foreign tenant" ? Guid.NewGuid() : tenant);
        var tournament = await h.TournamentAsync(org);
        var current = Seed(h, org, "Actual");
        var candidate = Seed(h, org, "Nuevo");
        h.Profiles.TournamentProfiles[tournament.Id] = current.Id;
        var (tournamentId, profileId) = scenario switch
        {
            "unknown tournament" => (Guid.NewGuid(), candidate.Id.Value),
            "profile of a sibling organization" => (tournament.Id, Seed(h, sibling, "Ajeno").Id.Value),
            "profile of a foreign organization" => (tournament.Id, Seed(h, Guid.NewGuid(), "Ajeno").Id.Value),
            "unknown profile" => (tournament.Id, Guid.NewGuid()),
            _ => (tournament.Id, candidate.Id.Value),
        };
        if (scenario == "profile deleted before the write")
            h.Profiles.BeforeNextWrite = () => h.Profiles.Remove(candidate.Id);
        if (scenario == "tournament deleted before the write")
            h.Profiles.BeforeNextWrite = () => h.Tournaments.Tournaments.DeleteAsync(tournament.Id).GetAwaiter().GetResult();

        var result = await AdminEligibilityProfilesEndpointsExtensions.SetTournamentProfileAsync(
            tournamentId, new TournamentEligibilityProfileBody(profileId), h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Profiles.Writes);
        Assert.Equal(current.Id, h.Profiles.TournamentProfiles[tournament.Id]);
    }

    [Theory]
    [InlineData("unknown tournament", StatusCodes.Status404NotFound, "tournaments.not_found")]
    [InlineData("foreign tenant", StatusCodes.Status403Forbidden, "tournaments.forbidden")]
    public async Task Set_ClearRefused_LeavesTheStoredProfileUnchanged(string scenario, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = scenario == "foreign tenant" ? await h.ForeignOrganizationAsync() : await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var current = Seed(h, org, "Actual");
        h.Profiles.TournamentProfiles[tournament.Id] = current.Id;

        var result = await AdminEligibilityProfilesEndpointsExtensions.SetTournamentProfileAsync(
            scenario == "unknown tournament" ? Guid.NewGuid() : tournament.Id, new TournamentEligibilityProfileBody(null), h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
        Assert.Empty(h.Profiles.Writes);
        Assert.Equal(current.Id, h.Profiles.TournamentProfiles[tournament.Id]);
    }

    [Theory]
    [InlineData("unknown tournament", StatusCodes.Status404NotFound, "tournaments.not_found")]
    [InlineData("foreign tenant", StatusCodes.Status403Forbidden, "tournaments.forbidden")]
    public async Task Get_Refused(string scenario, int status, string code)
    {
        var h = new RegistrationsTestHarness();
        var org = scenario == "foreign tenant" ? await h.ForeignOrganizationAsync() : await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        h.Profiles.TournamentProfiles[tournament.Id] = Seed(h, org, "Actual").Id;

        var result = await AdminEligibilityProfilesEndpointsExtensions.GetTournamentProfileAsync(
            scenario == "unknown tournament" ? Guid.NewGuid() : tournament.Id, h.Sender, CancellationToken.None);

        AssertProblem(result, status, code);
    }

    [Fact]
    public async Task TournamentEndpoints_KeepTheirContract_AndAnUpdateKeepsTheAssignedProfile()
    {
        var h = new RegistrationsTestHarness();
        var org = await h.OwnOrganizationAsync();
        var tournament = await h.TournamentAsync(org);
        var profile = Seed(h, org, "Mixto 4ª");
        await AdminEligibilityProfilesEndpointsExtensions.SetTournamentProfileAsync(
            tournament.Id, new TournamentEligibilityProfileBody(profile.Id.Value), h.Sender, CancellationToken.None);
        var update = new UpdateTournamentRequest(
            "Open B", null, null, tournament.SportId, null, null, tournament.ModalityId, null, null, null,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
            null, null, null, null, null, true, null);

        var updated = await AdminTournamentsEndpointsExtensions.UpdateAsync(tournament.Id, update, h.Sender, CancellationToken.None);
        var got = await AdminTournamentsEndpointsExtensions.GetByIdAsync(tournament.Id, h.Sender, CancellationToken.None);

        Assert.Equal("Open B", Assert.IsType<Ok<TournamentResponse>>(updated).Value!.Name);
        Assert.Equal("Open B", Assert.IsType<Ok<TournamentResponse>>(got).Value!.Name);
        Assert.Equal(profile.Id, h.Profiles.TournamentProfiles[tournament.Id]);
        foreach (var contract in new[] { typeof(CreateTournamentRequest), typeof(UpdateTournamentRequest), typeof(TournamentResponse) })
            Assert.Null(contract.GetProperty("EligibilityProfileId"));
    }
}
