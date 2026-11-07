using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.AdminTournaments.Create;
using ZonarHub.Application.Features.AdminTournaments.Delete;
using ZonarHub.Application.Features.AdminTournaments.GetById;
using ZonarHub.Application.Features.AdminTournaments.Update;
using ZonarHub.Domain.Users;

namespace ZonarHub.Tests.Application.AdminTournaments;

/// <summary>
/// Tenant rule for get-by-id, create, update and delete: system administrators reach any organization,
/// other callers only organizations of their own tenant, and a refused call never touches the store.
/// </summary>
public class AdminTournamentsItemAccessTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetById_SystemAdmin_CanReadAnotherTenantsTournament()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await h.GetById.Handle(new GetTournamentByIdQuery(tournament.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(tournament.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetById_TenantAdmin_CanReadOwnTenantTournament()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await h.GetById.Handle(new GetTournamentByIdQuery(tournament.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(tournament.Id, result.Value.Id);
    }

    [Fact]
    public async Task GetById_TenantAdmin_AnotherTenantsTournament_IsForbidden()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var tournament = h.Tournaments.Seed(foreignOrg.Id.Value, "Foreign Open");
        await AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await h.GetById.Handle(new GetTournamentByIdQuery(tournament.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task GetById_UnknownTournament_IsNotFound()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        await AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await h.GetById.Handle(new GetTournamentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Create_SystemAdmin_CanCreateInAnotherTenantsOrganization()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        await AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await h.Create.Handle(CreateCommand(org.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(org.Id.Value, Assert.Single(h.Tournaments.Added).OrganizationId);
    }

    [Fact]
    public async Task Create_TenantAdmin_CanCreateInOwnTenantOrganization()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        await AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await h.Create.Handle(CreateCommand(org.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(org.Id.Value, Assert.Single(h.Tournaments.Added).OrganizationId);
    }

    [Fact]
    public async Task Create_TenantAdmin_AnotherTenantsOrganization_IsForbiddenWithoutPersisting()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        await AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await h.Create.Handle(CreateCommand(foreignOrg.Id.Value), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Create_UnknownOrganization_IsForbiddenWithoutPersisting()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        await AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await h.Create.Handle(CreateCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Create_TenantAdmin_AnotherTenantsOrganizationWithInvalidName_IsForbidden()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        await AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await h.Create.Handle(CreateCommand(foreignOrg.Id.Value) with { Name = " " }, CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Create_EmptyOrganizationId_FailsWithOrganizationRequired()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        await AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await h.Create.Handle(CreateCommand(Guid.Empty), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.OrganizationRequired, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Create_OwnTenantOrganizationWithInvalidName_FailsWithNameRequired()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        await AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await h.Create.Handle(CreateCommand(org.Id.Value) with { Name = " " }, CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.NameRequired, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Update_SystemAdmin_CanUpdateAnotherTenantsTournament()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await h.Update.Handle(UpdateCommand(tournament.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed Open", Assert.Single(h.Tournaments.Updated).Name);
    }

    [Fact]
    public async Task Update_TenantAdmin_CanUpdateOwnTenantTournament_KeepingItsOrganization()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await h.Update.Handle(UpdateCommand(tournament.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var updated = Assert.Single(h.Tournaments.Updated);
        Assert.Equal("Renamed Open", updated.Name);
        Assert.Equal(org.Id.Value, updated.OrganizationId);
    }

    [Fact]
    public async Task Update_TenantAdmin_AnotherTenantsTournament_IsForbiddenWithoutPersisting()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var tournament = h.Tournaments.Seed(foreignOrg.Id.Value, "Foreign Open");
        await AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await h.Update.Handle(UpdateCommand(tournament.Id), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Update_TenantAdmin_AnotherTenantsTournamentWithInvalidName_IsForbidden()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var tournament = h.Tournaments.Seed(foreignOrg.Id.Value, "Foreign Open");
        await AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await h.Update.Handle(UpdateCommand(tournament.Id) with { Name = " " }, CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Update_UnknownTournament_IsNotFound()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        await AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await h.Update.Handle(UpdateCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.NotFound, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Update_OwnTenantTournamentWithInvalidName_FailsWithNameRequired()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await h.Update.Handle(UpdateCommand(tournament.Id) with { Name = " " }, CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.NameRequired, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task Delete_SystemAdmin_CanDeleteAnotherTenantsTournament()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await h.Delete.Handle(new DeleteTournamentCommand(tournament.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([tournament.Id], h.Tournaments.DeletedIds);
    }

    [Fact]
    public async Task Delete_TenantAdmin_CanDeleteOwnTenantTournament()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        await AuthenticateAsync(h, UserRole.Admin, tenant);

        var result = await h.Delete.Handle(new DeleteTournamentCommand(tournament.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([tournament.Id], h.Tournaments.DeletedIds);
    }

    [Fact]
    public async Task Delete_TenantAdmin_AnotherTenantsTournament_IsForbiddenWithoutDeleting()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var tournament = h.Tournaments.Seed(foreignOrg.Id.Value, "Foreign Open");
        await AuthenticateAsync(h, UserRole.Admin, Guid.NewGuid());

        var result = await h.Delete.Handle(new DeleteTournamentCommand(tournament.Id), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        AssertNoWrites(h);
        Assert.NotNull(await h.Tournaments.GetByIdAsync(tournament.Id));
    }

    [Fact]
    public async Task Delete_UnknownTournament_IsNotFound()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        await AuthenticateAsync(h, UserRole.SystemAdmin, tenantId: null);

        var result = await h.Delete.Handle(new DeleteTournamentCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.NotFound, result.Error);
        AssertNoWrites(h);
    }

    [Fact]
    public async Task UnauthenticatedCaller_IsForbiddenOnEveryItemOperation()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");

        Assert.Equal(AdminTournamentErrors.Forbidden, (await h.GetById.Handle(new GetTournamentByIdQuery(tournament.Id), CancellationToken.None)).Error);
        Assert.Equal(AdminTournamentErrors.Forbidden, (await h.Create.Handle(CreateCommand(org.Id.Value), CancellationToken.None)).Error);
        Assert.Equal(AdminTournamentErrors.Forbidden, (await h.Update.Handle(UpdateCommand(tournament.Id), CancellationToken.None)).Error);
        Assert.Equal(AdminTournamentErrors.Forbidden, (await h.Delete.Handle(new DeleteTournamentCommand(tournament.Id), CancellationToken.None)).Error);
        AssertNoWrites(h);
    }

    internal static async Task AuthenticateAsync(AdminTournamentsListTestHarness h, UserRole role, Guid? tenantId)
    {
        var caller = await h.Users.SeedUserAsync(role, tenantId, "caller@zonarhub.dev", "Caller");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);
    }

    internal static CreateTournamentCommand CreateCommand(Guid organizationId) => new(
        organizationId, "Open Cup", null, null, Guid.NewGuid(), null, null, null, null, null, null,
        new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
        null, null, null, null, null, true, []);

    internal static UpdateTournamentCommand UpdateCommand(Guid id) => new(
        id, "Renamed Open", null, null, Guid.NewGuid(), null, null, null, null, null, null,
        new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
        null, null, null, null, null, true, []);

    private static void AssertNoWrites(AdminTournamentsListTestHarness h)
    {
        Assert.Empty(h.Tournaments.Added);
        Assert.Empty(h.Tournaments.Updated);
        Assert.Empty(h.Tournaments.DeletedIds);
    }
}
