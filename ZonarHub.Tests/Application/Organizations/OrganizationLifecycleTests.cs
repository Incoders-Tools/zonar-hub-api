using ZonarHub.Application.Features.Organizations.Create;
using ZonarHub.Application.Features.Organizations.Delete;
using ZonarHub.Application.Features.Organizations.GetAll;
using ZonarHub.Application.Features.Organizations.GetById;
using ZonarHub.Application.Features.Organizations.Update;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Tests.Application.Organizations;

public class OrganizationLifecycleTests
{
    private static readonly DateTime Now = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task FullLifecycle_CreateReadUpdateDelete_Works()
    {
        var h = new OrganizationsTestHarness(Now);

        // Create
        var created = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Padel Club", null, null, OrganizationType.Circuito, null, UserId),
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal("Padel Club", created.Value.DisplayName);
        Assert.Equal("circuito", created.Value.Type);

        // Get by id (same tenant)
        var fetched = await h.GetOrgById.Handle(
            new GetOrganizationByIdQuery(created.Value.Id, TenantA),
            CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(created.Value.Id, fetched.Value.Id);

        // List by tenant
        var listed = await h.ListOrgs.Handle(
            new GetOrganizationsQuery(new OrganizationFilter(TenantId: TenantA)),
            CancellationToken.None);
        Assert.True(listed.IsSuccess);
        Assert.Equal(1, listed.Value.TotalCount);

        // Update
        h.Clock.UtcNow = Now.AddHours(1);
        var updated = await h.UpdateOrg.Handle(
            new UpdateOrganizationCommand(created.Value.Id, "Padel Club Pro", null, null, OrganizationType.Circuito, null, true, TenantA),
            CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Padel Club Pro", updated.Value.DisplayName);
        Assert.Equal(Now.AddHours(1), updated.Value.UpdatedAt);

        // Delete
        var deleted = await h.DeleteOrg.Handle(
            new DeleteOrganizationCommand(created.Value.Id, TenantA),
            CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        // Confirm gone
        var missing = await h.GetOrgById.Handle(
            new GetOrganizationByIdQuery(created.Value.Id, TenantA),
            CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("organizations.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task GetById_CrossTenantAccess_IsDenied()
    {
        var h = new OrganizationsTestHarness(Now);
        var tenantB = Guid.NewGuid();

        var created = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Club A", null, null, OrganizationType.Estandar, null, UserId),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var crossTenant = await h.GetOrgById.Handle(
            new GetOrganizationByIdQuery(created.Value.Id, tenantB),
            CancellationToken.None);

        Assert.True(crossTenant.IsFailure);
        Assert.Equal("organizations.cross_tenant_access_denied", crossTenant.Error.Code);
    }

    [Fact]
    public async Task List_FilterByIsActive_ReturnsOnlyMatching()
    {
        var h = new OrganizationsTestHarness(Now);

        var org = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Active Club", null, null, OrganizationType.Estandar, null, UserId),
            CancellationToken.None);

        await h.UpdateOrg.Handle(
            new UpdateOrganizationCommand(org.Value.Id, "Active Club", null, null, OrganizationType.Estandar, null, false, TenantA),
            CancellationToken.None);

        await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Another Club", null, null, OrganizationType.Estandar, null, UserId),
            CancellationToken.None);

        var activeOnly = await h.ListOrgs.Handle(
            new GetOrganizationsQuery(new OrganizationFilter(TenantId: TenantA, IsActive: true)),
            CancellationToken.None);

        Assert.True(activeOnly.IsSuccess);
        Assert.Equal(1, activeOnly.Value.TotalCount);
        Assert.All(activeOnly.Value.Items, o => Assert.True(o.IsActive));
    }

    [Fact]
    public async Task CreateOrganization_WithDuplicateDisplayNameInSameTenant_ReturnsConflict()
    {
        var h = new OrganizationsTestHarness(Now);

        var first = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "Padel Club", null, null, OrganizationType.Circuito, null, UserId),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var duplicate = await h.CreateOrg.Handle(
            new CreateOrganizationCommand(TenantA, "padel club", null, null, OrganizationType.Circuito, null, UserId),
            CancellationToken.None);

        Assert.True(duplicate.IsFailure);
        Assert.Equal("organizations.duplicate_display_name", duplicate.Error.Code);
    }
}
