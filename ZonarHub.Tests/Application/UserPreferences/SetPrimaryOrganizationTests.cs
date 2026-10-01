using ZonarHub.Application.Features.UserPreferences.SetPrimaryOrganization;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.InMemory;
using ZonarHub.Tests.Common;
using ZonarHub.Tests.Application.AdminUsers;

namespace ZonarHub.Tests.Application.UserPreferences;

public sealed class SetPrimaryOrganizationTests
{
    [Fact]
    public async Task Assigned_active_organization_becomes_persisted_primary_without_changing_assignments()
    {
        var scenario = await Scenario.CreateAsync();
        var alternate = await scenario.AddOrganizationAsync();
        await scenario.AssignAsync(scenario.Organization.Id.Value, alternate.Id.Value);

        var result = await scenario.SelectAsync(alternate.Id.Value);

        Assert.True(result.IsSuccess);
        Assert.Equal(alternate.Id.Value, (await scenario.Users.GetByIdAsync(scenario.User.Id))!.OrganizationId);
        Assert.Equal([scenario.Organization.Id.Value, alternate.Id.Value], await scenario.AssignedAsync());
    }

    [Fact]
    public async Task Selecting_existing_primary_is_a_no_op()
    {
        var scenario = await Scenario.CreateAsync();
        Assert.True((await scenario.SelectAsync(scenario.Organization.Id.Value)).IsSuccess);
        var originalUpdate = scenario.User.UpdatedAtUtc;

        var result = await scenario.SelectAsync(scenario.Organization.Id.Value);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalUpdate, scenario.User.UpdatedAtUtc);
        Assert.Equal([scenario.Organization.Id.Value], await scenario.AssignedAsync());
    }

    [Theory]
    [InlineData("unassigned")]
    [InlineData("inactive")]
    [InlineData("cross-tenant")]
    [InlineData("missing")]
    [InlineData("empty")]
    public async Task Ineligible_organization_does_not_change_primary_or_assignments(string condition)
    {
        var scenario = await Scenario.CreateAsync();
        Assert.True((await scenario.SelectAsync(scenario.Organization.Id.Value)).IsSuccess);
        var candidate = await scenario.AddOrganizationAsync(condition == "cross-tenant" ? Guid.NewGuid() : null);
        if (condition != "unassigned")
            await scenario.AssignAsync(scenario.Organization.Id.Value, candidate.Id.Value);
        if (condition == "inactive")
            candidate.Update(candidate.DisplayName, null, null, candidate.Type, null, false, scenario.Clock.UtcNow);
        var before = await scenario.AssignedAsync();
        var id = condition switch { "missing" => Guid.NewGuid(), "empty" => Guid.Empty, _ => candidate.Id.Value };

        var result = await scenario.SelectAsync(id);

        Assert.True(result.IsFailure);
        Assert.Equal("PrimaryOrganization.Ineligible", result.Error.Code);
        Assert.Equal(scenario.Organization.Id.Value, scenario.User.OrganizationId);
        Assert.Equal(before, await scenario.AssignedAsync());
    }

    [Theory]
    [InlineData(UserRole.Player, true)]
    [InlineData(UserRole.Admin, false)]
    public async Task Non_admin_or_unauthenticated_caller_cannot_change_primary(UserRole role, bool authenticated)
    {
        var scenario = await Scenario.CreateAsync(role);
        if (!authenticated) scenario.Current.SignOut();

        var result = await scenario.SelectAsync(scenario.Organization.Id.Value);

        Assert.True(result.IsFailure);
        Assert.Equal(authenticated ? "PrimaryOrganization.Forbidden" : "PrimaryOrganization.Unauthorized", result.Error.Code);
        Assert.Null(scenario.User.OrganizationId);
        Assert.Equal([scenario.Organization.Id.Value], await scenario.AssignedAsync());
    }

    private sealed class Scenario
    {
        public readonly TestClock Clock = new(DateTime.UtcNow);
        public readonly TestCurrentUser Current = new();
        public readonly InMemoryUserRepository Users = new(new InMemoryUserStore());
        public readonly InMemoryOrganizationRepository Organizations = new(new InMemoryOrganizationStore());
        public readonly InMemoryUserOrganizationAssignmentRepository Assignments = new(new InMemoryUserOrganizationAssignmentStore());
        public User User { get; private set; } = null!;
        public Organization Organization { get; private set; } = null!;

        public static async Task<Scenario> CreateAsync(UserRole role = UserRole.Admin)
        {
            var scenario = new Scenario();
            var tenant = Guid.NewGuid();
            var user = User.Register(UserId.New(), "admin@example.com", "Admin", null, null, "hash", role, tenant, scenario.Clock.UtcNow).Value;
            await scenario.Users.AddAsync(user);
            scenario.User = user;
            var organization = await scenario.AddOrganizationAsync(tenant);
            await scenario.AssignAsync(organization.Id.Value);
            scenario.Current.Authenticate(user.Id.Value, user.Email);
            scenario.Organization = organization;
            return scenario;
        }

        public async Task<Organization> AddOrganizationAsync(Guid? tenant = null)
        {
            var organization = Organization.Create(OrganizationId.New(), tenant ?? User.TenantId!.Value, "Club", null, null,
                OrganizationType.Estandar, null, Guid.NewGuid(), Clock.UtcNow).Value;
            await Organizations.AddAsync(organization);
            return organization;
        }

        public Task AssignAsync(params Guid[] ids) => Assignments.SetOrganizationIdsAsync(User.Id.Value, ids);
        public Task<IReadOnlyList<Guid>> AssignedAsync() => Assignments.GetOrganizationIdsByUserIdAsync(User.Id.Value);
        public Task<ZonarHub.Domain.Common.Result> SelectAsync(Guid id) =>
            new SetPrimaryOrganizationHandler(Current, Users, Assignments, Organizations, new InMemoryUnitOfWork(), Clock)
                .Handle(new SetPrimaryOrganizationCommand(id), CancellationToken.None);
    }
}
