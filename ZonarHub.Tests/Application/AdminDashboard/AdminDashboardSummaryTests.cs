using ZonarHub.Application.Features.AdminDashboard.GetSummary;
using ZonarHub.Domain.Tournaments;
using ZonarHub.Domain.Users;

namespace ZonarHub.Tests.Application.AdminDashboard;

public class AdminDashboardSummaryTests
{
    private static readonly DateTime Now = new(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetSummary_ReturnsOrganizationScopedMetrics()
    {
        var h = new AdminDashboardSummaryTestHarness(Now);
        var tenantId = Guid.NewGuid();

        var organizationA = await h.SeedOrganizationAsync(tenantId, "Organization A");
        var organizationB = await h.SeedOrganizationAsync(tenantId, "Organization B");

        var complexA1 = await h.SeedComplexAsync(organizationA, "A1");
        var complexA2 = await h.SeedComplexAsync(organizationA, "A2");
        var complexB1 = await h.SeedComplexAsync(organizationB, "B1");

        await h.SeedCourtAsync(complexA1, "Court A1-1");
        await h.SeedCourtAsync(complexA1, "Court A1-2");
        await h.SeedCourtAsync(complexA2, "Court A2-1");
        await h.SeedCourtAsync(complexB1, "Court B1-1");

        await h.SeedTournamentAsync(organizationA, "A Upcoming", TournamentStatus.Upcoming, complexA1);
        await h.SeedTournamentAsync(organizationA, "A Active", TournamentStatus.Active, complexA2);
        await h.SeedTournamentAsync(organizationA, "A Finished", TournamentStatus.Finished, complexA1);
        await h.SeedTournamentAsync(organizationA, "A Cancelled", TournamentStatus.Cancelled, complexA2);
        await h.SeedTournamentAsync(organizationB, "B Active", TournamentStatus.Active, complexB1);

        await h.SeedRegistrationsAsync(organizationA, count: 5);
        await h.SeedRegistrationsAsync(organizationB, count: 2);

        await h.SetEnabledSportsAsync(organizationA, count: 2);
        await h.SetEnabledSportsAsync(organizationB, count: 1);

        var adminA = await h.SeedUserAsync(UserRole.Admin, "admin-a@zonarhub.dev", organizationA);
        var adminAssigned = await h.SeedUserAsync(UserRole.Admin, "admin-assigned@zonarhub.dev", organizationB);
        await h.AssignOrganizationsAsync(adminAssigned, organizationA, organizationB);
        await h.SeedUserAsync(UserRole.Admin, "admin-b@zonarhub.dev", organizationB);

        var playerA = await h.SeedUserAsync(UserRole.Player, "player-a@zonarhub.dev", organizationA);
        var playerAssigned = await h.SeedUserAsync(UserRole.Player, "player-assigned@zonarhub.dev", organizationB);
        await h.AssignOrganizationsAsync(playerAssigned, organizationA);
        await h.SeedUserAsync(UserRole.Player, "player-b@zonarhub.dev", organizationB);
        await h.SeedUserAsync(UserRole.Viewer, "viewer-a@zonarhub.dev", organizationA);

        var result = await h.Summary.Handle(
            new GetAdminDashboardSummaryQuery(organizationA.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(organizationA.Id.Value, result.Value.OrganizationId);
        Assert.Equal(4, result.Value.TotalTournaments);
        Assert.Equal(2, result.Value.ActiveTournaments);
        Assert.Equal(1, result.Value.FinishedTournaments);
        Assert.Equal(2, result.Value.ComplexCount);
        Assert.Equal(3, result.Value.CourtCount);
        Assert.Equal(2, result.Value.ActiveSportsCount);
        Assert.Equal(2, result.Value.AdminCount);
        Assert.Equal(2, result.Value.TotalPlayers);
        Assert.Equal(5, result.Value.TotalRegistrations);

        Assert.NotNull(adminA);
        Assert.NotNull(playerA);
    }

    [Fact]
    public async Task GetSummary_EmptyOrganizationId_ReturnsValidationError()
    {
        var h = new AdminDashboardSummaryTestHarness(Now);

        var result = await h.Summary.Handle(
            new GetAdminDashboardSummaryQuery(Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin_dashboard.organization_required", result.Error.Code);
    }

    [Fact]
    public async Task GetSummary_UnknownOrganization_ReturnsNotFound()
    {
        var h = new AdminDashboardSummaryTestHarness(Now);

        var result = await h.Summary.Handle(
            new GetAdminDashboardSummaryQuery(Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("admin_dashboard.organization_not_found", result.Error.Code);
    }
}
