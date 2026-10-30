using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminTournaments;
using ZonarHub.Application.Features.AdminTournaments.GetAll;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminUsers;

namespace ZonarHub.Tests.Application.AdminTournaments;

public class AdminTournamentsListAccessTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SystemAdmin_CanListAnotherTenantsOrganization()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var caller = await h.Users.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(new GetTournamentsQuery(org.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(tournament.Id, Assert.Single(result.Value).Id);
        Assert.Equal([org.Id.Value], h.Tournaments.ListedOrganizationIds);
    }

    [Fact]
    public async Task TenantAdmin_CanListOwnTenantOrganization()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var tenant = Guid.NewGuid();
        var caller = await h.Users.SeedUserAsync(UserRole.Admin, tenant, "admin@zonarhub.dev", "Admin");
        var org = await h.Users.SeedOrganizationAsync(tenant, "Org A");
        var tournament = h.Tournaments.Seed(org.Id.Value, "Open A");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(new GetTournamentsQuery(org.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(tournament.Id, Assert.Single(result.Value).Id);
    }

    [Fact]
    public async Task TenantAdmin_AnotherTenantsOrganization_IsForbiddenWithoutListing()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var caller = await h.Users.SeedUserAsync(UserRole.Admin, Guid.NewGuid(), "admin@zonarhub.dev", "Admin");
        var foreignOrg = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        h.Tournaments.Seed(foreignOrg.Id.Value, "Foreign Open");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(new GetTournamentsQuery(foreignOrg.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        Assert.Empty(h.Tournaments.ListedOrganizationIds);
    }

    [Fact]
    public async Task TenantAdminWithoutTenant_IsForbiddenWithoutListing()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var caller = await h.Users.SeedUserAsync(UserRole.Admin, tenantId: null, "admin@zonarhub.dev", "Admin");
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(new GetTournamentsQuery(org.Id.Value), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        Assert.Empty(h.Tournaments.ListedOrganizationIds);
    }

    [Theory]
    [InlineData(UserRole.SystemAdmin)]
    [InlineData(UserRole.Admin)]
    public async Task UnknownOrganization_IsForbiddenWithoutListing(UserRole role)
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var caller = await h.Users.SeedUserAsync(role, Guid.NewGuid(), "admin@zonarhub.dev", "Admin");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(new GetTournamentsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        Assert.Empty(h.Tournaments.ListedOrganizationIds);
    }

    [Fact]
    public async Task UnauthenticatedCaller_IsForbiddenWithoutListing()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");

        var result = await h.List.Handle(new GetTournamentsQuery(org.Id.Value), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        Assert.Empty(h.Tournaments.ListedOrganizationIds);
    }

    [Fact]
    public async Task UnknownCaller_IsForbiddenWithoutListing()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var org = await h.Users.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        h.Users.CurrentUser.Authenticate(Guid.NewGuid(), "ghost@zonarhub.dev");

        var result = await h.List.Handle(new GetTournamentsQuery(org.Id.Value), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.Forbidden, result.Error);
        Assert.Empty(h.Tournaments.ListedOrganizationIds);
    }

    [Fact]
    public async Task EmptyOrganizationId_FailsWithOrganizationRequired()
    {
        var h = new AdminTournamentsListTestHarness(Now);
        var caller = await h.Users.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        h.Users.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(new GetTournamentsQuery(Guid.Empty), CancellationToken.None);

        Assert.Equal(AdminTournamentErrors.OrganizationRequired, result.Error);
        Assert.Empty(h.Tournaments.ListedOrganizationIds);
    }
}

/// <summary>
/// Wires the real list handler to the in-memory user/organization stores and a recording tournament repository.
/// </summary>
internal sealed class AdminTournamentsListTestHarness
{
    public AdminTournamentsListTestHarness(DateTime nowUtc)
    {
        Users = new AdminUsersTestHarness(nowUtc);
        Tournaments = new RecordingTournamentAdminRepository(nowUtc);
        List = new GetTournamentsHandler(Users.CurrentUser, Users.Users, Users.Organizations, Tournaments);
    }

    public AdminUsersTestHarness Users { get; }
    public RecordingTournamentAdminRepository Tournaments { get; }
    public GetTournamentsHandler List { get; }
}

internal sealed class RecordingTournamentAdminRepository(DateTime nowUtc) : ITournamentAdminRepository
{
    private readonly List<TournamentAdminDto> _rows = [];

    public List<Guid> ListedOrganizationIds { get; } = [];

    public TournamentAdminDto Seed(Guid organizationId, string name)
    {
        var row = new TournamentAdminDto(
            Guid.NewGuid(), organizationId, name, null, null, Guid.NewGuid(), null, null, null, null, null, null,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 2), null, null, null, null, null, null, null,
            null, null, null, null, null, true, [], nowUtc, nowUtc);
        _rows.Add(row);
        return row;
    }

    public Task<IReadOnlyList<TournamentAdminDto>> ListByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        ListedOrganizationIds.Add(organizationId);
        IReadOnlyList<TournamentAdminDto> rows = _rows.Where(row => row.OrganizationId == organizationId).ToList();
        return Task.FromResult(rows);
    }

    public Task<TournamentAdminDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TournamentAdminDto> AddAsync(TournamentAdminDto tournament, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<TournamentAdminDto> UpdateAsync(TournamentAdminDto tournament, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
