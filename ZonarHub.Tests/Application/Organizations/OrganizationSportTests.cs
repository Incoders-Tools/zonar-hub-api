using ZonarHub.Application.Features.Organizations.Create;
using ZonarHub.Application.Features.OrganizationSports.Get;
using ZonarHub.Application.Features.OrganizationSports.Set;
using ZonarHub.Application.Features.Sports.Create;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Tests.Application.Organizations;

public class OrganizationSportTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task SetAndGet_EnabledSports_WorksCorrectly()
    {
        var h = new OrganizationsTestHarness(Now);

        var org = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Club A", null, null, OrganizationType.Circuito, null, UserId),
            CancellationToken.None);
        Assert.True(org.IsSuccess);

        var padel = await h.CreateSport.Handle(
            new CreateSportCommand("Padel", "padel", "🎾", SportIconSource.Unicode, null, 1),
            CancellationToken.None);
        var tennis = await h.CreateSport.Handle(
            new CreateSportCommand("Tennis", "tennis", "🎾", SportIconSource.Unicode, null, 2),
            CancellationToken.None);
        Assert.True(padel.IsSuccess);
        Assert.True(tennis.IsSuccess);

        // Enable only padel
        var set = await h.SetOrgSports.Handle(
            new SetOrganizationSportsCommand(org.Value.Id, new[] { padel.Value.Id }, TenantA),
            CancellationToken.None);
        Assert.True(set.IsSuccess);

        // Get: 2 sports, one enabled
        var result = await h.GetOrgSports.Handle(
            new GetOrganizationSportsQuery(org.Value.Id, TenantA),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);

        var padelEntry = result.Value.Items.Single(i => i.Key == "padel");
        var tennisEntry = result.Value.Items.Single(i => i.Key == "tennis");
        Assert.True(padelEntry.IsEnabled);
        Assert.False(tennisEntry.IsEnabled);
    }

    [Fact]
    public async Task SetSports_ReplacesPreviousSet()
    {
        var h = new OrganizationsTestHarness(Now);

        var org = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Club A", null, null, OrganizationType.Circuito, null, UserId),
            CancellationToken.None);

        var padel = await h.CreateSport.Handle(
            new CreateSportCommand("Padel", "padel", "🎾", SportIconSource.Unicode, null, 1),
            CancellationToken.None);
        var tennis = await h.CreateSport.Handle(
            new CreateSportCommand("Tennis", "tennis", "🎾", SportIconSource.Unicode, null, 2),
            CancellationToken.None);

        // Enable padel first
        await h.SetOrgSports.Handle(
            new SetOrganizationSportsCommand(org.Value.Id, new[] { padel.Value.Id }, TenantA),
            CancellationToken.None);

        // Now switch to tennis only
        await h.SetOrgSports.Handle(
            new SetOrganizationSportsCommand(org.Value.Id, new[] { tennis.Value.Id }, TenantA),
            CancellationToken.None);

        var result = await h.GetOrgSports.Handle(
            new GetOrganizationSportsQuery(org.Value.Id, TenantA),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var padelEntry = result.Value.Items.Single(i => i.Key == "padel");
        var tennisEntry = result.Value.Items.Single(i => i.Key == "tennis");
        Assert.False(padelEntry.IsEnabled);
        Assert.True(tennisEntry.IsEnabled);
    }

    [Fact]
    public async Task SetSports_CrossTenantOrganization_IsDenied()
    {
        var h = new OrganizationsTestHarness(Now);
        var tenantB = Guid.NewGuid();

        var org = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Club A", null, null, OrganizationType.Circuito, null, UserId),
            CancellationToken.None);

        var padel = await h.CreateSport.Handle(
            new CreateSportCommand("Padel", "padel", "🎾", SportIconSource.Unicode, null, 1),
            CancellationToken.None);

        var result = await h.SetOrgSports.Handle(
            new SetOrganizationSportsCommand(org.Value.Id, new[] { padel.Value.Id }, tenantB),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("organizations.cross_tenant_access_denied", result.Error.Code);
    }

    [Fact]
    public async Task SetSports_WithInvalidSportId_ReturnsBadRequest()
    {
        var h = new OrganizationsTestHarness(Now);

        var org = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Club A", null, null, OrganizationType.Circuito, null, UserId),
            CancellationToken.None);

        var nonExistentSportId = Guid.NewGuid();

        var result = await h.SetOrgSports.Handle(
            new SetOrganizationSportsCommand(org.Value.Id, new[] { nonExistentSportId }, TenantA),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("sports.not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetOrgSports_NonExistentOrganization_ReturnsNotFound()
    {
        var h = new OrganizationsTestHarness(Now);

        var result = await h.GetOrgSports.Handle(
            new GetOrganizationSportsQuery(Guid.NewGuid(), TenantA),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("organizations.not_found", result.Error.Code);
    }
}
