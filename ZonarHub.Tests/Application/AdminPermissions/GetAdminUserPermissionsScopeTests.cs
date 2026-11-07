using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Application.Features.AdminPermissions.GetUserPermissions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;
using ZonarHub.Tests.Application.AdminUsers;

namespace ZonarHub.Tests.Application.AdminPermissions;

public class GetAdminUserPermissionsScopeTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TenantAdmin_ExcludesForeignTenantAssignment_AndItsPersistedKeys()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, tenantId, assignedOrganizationIds: [foreignOrg.Id.Value, ownOrg.Id.Value]);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [
                new UserOrganizationPermission(ownOrg.Id.Value, "users.view"),
                new UserOrganizationPermission(foreignOrg.Id.Value, "users.manage"),
            ]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var organization = Assert.Single(result.Value.PermissionsByOrganization);
        Assert.Equal(ownOrg.Id.Value, organization.OrganizationId);
        Assert.Equal(new[] { "users.view" }, organization.ToolKeys);
        Assert.DoesNotContain(result.Value.PermissionsByOrganization, item => item.OrganizationId == foreignOrg.Id.Value);
        Assert.DoesNotContain(result.Value.PermissionsByOrganization, item => item.ToolKeys.Contains("users.manage"));
    }

    [Fact]
    public async Task TenantAdmin_ExcludesForeignTenantPrimaryOrganization()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(
            h,
            tenantId,
            primaryOrganizationId: foreignOrg.Id.Value,
            assignedOrganizationIds: [ownOrg.Id.Value]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { ownOrg.Id.Value },
            result.Value.PermissionsByOrganization.Select(item => item.OrganizationId).ToArray());
    }

    [Fact]
    public async Task TenantAdmin_ExcludesAssignmentWhoseOrganizationCannotBeFound()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var missingOrganizationId = Guid.NewGuid();
        var target = await SeedTargetAsync(
            h,
            tenantId,
            primaryOrganizationId: missingOrganizationId,
            assignedOrganizationIds: [ownOrg.Id.Value]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { ownOrg.Id.Value },
            result.Value.PermissionsByOrganization.Select(item => item.OrganizationId).ToArray());
    }

    [Fact]
    public async Task TenantAdmin_PreservesSameTenantPrimaryAndAssignments_WithPersistedKeysAndDefaults()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var primaryOrg = await h.SeedOrganizationAsync(tenantId, "Primary Org");
        var assignedOrg = await h.SeedOrganizationAsync(tenantId, "Assigned Org");
        var target = await SeedTargetAsync(
            h,
            tenantId,
            primaryOrganizationId: primaryOrg.Id.Value,
            assignedOrganizationIds: [assignedOrg.Id.Value]);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [
                new UserOrganizationPermission(assignedOrg.Id.Value, "users.view"),
                new UserOrganizationPermission(assignedOrg.Id.Value, "USERS.VIEW"),
            ]);
        var defaults = await h.PermissionService.GetDefaultToolKeysAsync(target.Role);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(target.Id.Value, result.Value.UserId);
        Assert.Collection(
            result.Value.PermissionsByOrganization,
            primary =>
            {
                Assert.Equal(primaryOrg.Id.Value, primary.OrganizationId);
                Assert.Equal(defaults, primary.ToolKeys);
            },
            assigned =>
            {
                Assert.Equal(assignedOrg.Id.Value, assigned.OrganizationId);
                Assert.Equal(new[] { "users.view" }, assigned.ToolKeys);
            });
    }

    [Fact]
    public async Task SystemAdmin_ReturnsEveryAssignedAndPrimaryOrganization_AcrossTenants()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var missingOrganizationId = Guid.NewGuid();
        var target = await SeedTargetAsync(
            h,
            tenantId,
            primaryOrganizationId: missingOrganizationId,
            assignedOrganizationIds: [ownOrg.Id.Value, foreignOrg.Id.Value]);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [new UserOrganizationPermission(foreignOrg.Id.Value, "users.manage")]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { missingOrganizationId, ownOrg.Id.Value, foreignOrg.Id.Value },
            result.Value.PermissionsByOrganization.Select(item => item.OrganizationId).ToArray());
        Assert.Equal(
            new[] { "users.manage" },
            result.Value.PermissionsByOrganization.Single(item => item.OrganizationId == foreignOrg.Id.Value).ToolKeys);
    }

    [Fact]
    public async Task TenantAdmin_TargetInForeignTenant_IsForbidden()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.Admin, Guid.NewGuid(), "admin@zonarhub.dev", "Admin");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, foreignOrg.TenantId, primaryOrganizationId: foreignOrg.Id.Value);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminPermissionErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task TenantAdmin_SystemAdminTargetInSameTenant_IsForbidden()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var target = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId, "root@zonarhub.dev", "Root");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminPermissionErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task Unauthenticated_FailsWithCallerNotAuthenticated()
    {
        var h = new AdminUsersTestHarness(Now);
        var target = await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "viewer@zonarhub.dev", "Viewer");

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminPermissionErrors.CallerNotAuthenticated, result.Error);
    }

    [Fact]
    public async Task UnknownCaller_FailsWithCallerNotFound()
    {
        var h = new AdminUsersTestHarness(Now);
        var target = await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "viewer@zonarhub.dev", "Viewer");
        h.CurrentUser.Authenticate(Guid.NewGuid(), "ghost@zonarhub.dev");

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminPermissionErrors.CallerNotFound, result.Error);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task UnknownTarget_FailsWithUserNotFound()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await Handler(h).Handle(new GetAdminUserPermissionsQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminPermissionErrors.UserNotFound, result.Error);
    }

    internal static GetAdminUserPermissionsHandler Handler(AdminUsersTestHarness h) =>
        new(h.CurrentUser, h.Users, h.Assignments, h.UserPermissions, h.PermissionService, h.Organizations);

    internal static async Task<User> SeedTargetAsync(
        AdminUsersTestHarness h,
        Guid tenantId,
        Guid? primaryOrganizationId = null,
        Guid[]? assignedOrganizationIds = null)
    {
        var user = await h.SeedUserAsync(UserRole.Viewer, tenantId, $"target-{Guid.NewGuid():N}@zonarhub.dev", "Target");
        if (primaryOrganizationId is { } primaryId)
        {
            Assert.True(user.AssignOrganization(primaryId, Now).IsSuccess);
            h.Users.Update(user);
        }

        if (assignedOrganizationIds is { Length: > 0 })
        {
            await h.Assignments.SetOrganizationIdsAsync(user.Id.Value, assignedOrganizationIds);
        }

        return user;
    }
}
