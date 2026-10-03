using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Application.Features.AdminUsers.Update;
using ZonarHub.Domain.Users;

namespace ZonarHub.Tests.Application.AdminUsers;

/// <summary>
/// The permission matrix GET hides a foreign-tenant primary organization from tenant admins,
/// while PUT replaces every permission row. These tests protect hidden foreign rows from being
/// silently deleted when a tenant admin resends the visible matrix.
/// </summary>
public class AdminUsersHiddenPrimaryPermissionUpdateTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TenantAdmin_PermissionOnlyUpdate_WithForeignPrimary_FailsWithoutMutation()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, tenantId, foreignOrg.Id.Value, ownOrg.Id.Value);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [
                new UserOrganizationPermission(ownOrg.Id.Value, "teams"),
                new UserOrganizationPermission(foreignOrg.Id.Value, "players"),
            ]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);
        h.Clock.UtcNow = Now.AddHours(1);

        var result = await h.Update.Handle(PermissionOnly(target.Id.Value, ownOrg.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid, result.Error);
        await AssertUnchangedAsync(h, target.Id, foreignOrg.Id.Value, ownOrg.Id.Value);
        var permissions = await h.UserPermissions.GetByUserIdAsync(target.Id.Value);
        Assert.Equal(
            new[] { (ownOrg.Id.Value, "teams"), (foreignOrg.Id.Value, "players") },
            permissions.Select(permission => (permission.OrganizationId, permission.ToolKey)).ToArray());
    }

    [Fact]
    public async Task TenantAdmin_PermissionOnlyUpdate_WithMissingPrimary_FailsWithoutMutation()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var missingOrganizationId = Guid.NewGuid();
        var target = await SeedTargetAsync(h, tenantId, missingOrganizationId, ownOrg.Id.Value);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [new UserOrganizationPermission(missingOrganizationId, "players")]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);
        h.Clock.UtcNow = Now.AddHours(1);

        var result = await h.Update.Handle(PermissionOnly(target.Id.Value, ownOrg.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid, result.Error);
        await AssertUnchangedAsync(h, target.Id, missingOrganizationId, ownOrg.Id.Value);
        var permission = Assert.Single(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
        Assert.Equal(missingOrganizationId, permission.OrganizationId);
    }

    [Fact]
    public async Task TenantAdmin_ExplicitSameTenantPrimary_WithStoredForeignPermissionRows_FailsWithoutMutation()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, tenantId, foreignOrg.Id.Value, ownOrg.Id.Value);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [new UserOrganizationPermission(foreignOrg.Id.Value, "players")]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);
        h.Clock.UtcNow = Now.AddHours(1);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, ownOrg.Id.Value) with { OrganizationId = ownOrg.Id.Value },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid, result.Error);
        await AssertUnchangedAsync(h, target.Id, foreignOrg.Id.Value, ownOrg.Id.Value);
        var permission = Assert.Single(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
        Assert.Equal((foreignOrg.Id.Value, "players"), (permission.OrganizationId, permission.ToolKey));
    }

    [Fact]
    public async Task TenantAdmin_ExplicitSameTenantPrimary_WithoutStoredForeignRows_RepairsForeignPrimary()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, tenantId, foreignOrg.Id.Value, ownOrg.Id.Value);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, ownOrg.Id.Value) with { OrganizationId = ownOrg.Id.Value },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await h.Users.GetByIdAsync(target.Id);
        Assert.Equal(ownOrg.Id.Value, stored!.OrganizationId);
        Assert.Equal(
            new[] { ownOrg.Id.Value },
            (await h.Assignments.GetOrganizationIdsByUserIdAsync(target.Id.Value)).ToArray());
        var permission = Assert.Single(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
        Assert.Equal((ownOrg.Id.Value, "teams"), (permission.OrganizationId, permission.ToolKey));
    }

    [Fact]
    public async Task TenantAdmin_PermissionOnlyUpdate_WithSameTenantPrimary_Saves()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var target = await SeedTargetAsync(h, tenantId, ownOrg.Id.Value, ownOrg.Id.Value);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [new UserOrganizationPermission(ownOrg.Id.Value, "players")]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Update.Handle(PermissionOnly(target.Id.Value, ownOrg.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ownOrg.Id.Value, result.Value.OrganizationId);
        var permission = Assert.Single(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
        Assert.Equal((ownOrg.Id.Value, "teams"), (permission.OrganizationId, permission.ToolKey));
    }

    [Fact]
    public async Task SystemAdmin_PermissionOnlyUpdate_WithForeignPrimary_KeepsFullBehavior()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, tenantId, foreignOrg.Id.Value, ownOrg.Id.Value);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [new UserOrganizationPermission(foreignOrg.Id.Value, "players")]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Update.Handle(PermissionOnly(target.Id.Value, ownOrg.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(foreignOrg.Id.Value, result.Value.OrganizationId);
        var permission = Assert.Single(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
        Assert.Equal((ownOrg.Id.Value, "teams"), (permission.OrganizationId, permission.ToolKey));
    }

    [Fact]
    public async Task SystemAdmin_ProfileUpdate_WithMissingPrimary_KeepsFullBehavior()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var missingOrganizationId = Guid.NewGuid();
        var target = await SeedTargetAsync(h, tenantId, missingOrganizationId, ownOrg.Id.Value);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, ownOrg.Id.Value) with { PermissionsByOrganization = null },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(missingOrganizationId, result.Value.OrganizationId);
        Assert.Equal("Changed Name", result.Value.FullName);
    }

    [Fact]
    public async Task TenantAdmin_ProfileUpdate_WithoutPrimaryOrAssignments_Saves()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var target = await h.SeedUserAsync(UserRole.Viewer, tenantId, "target@zonarhub.dev", "Target");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, Guid.NewGuid()) with { PermissionsByOrganization = null },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.OrganizationId);
        Assert.Equal("Changed Name", result.Value.FullName);
    }

    [Fact]
    public async Task TenantAdmin_ExplicitTenantIds_WithStoredForeignAssignment_FailsWithoutMutation()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, tenantId, ownOrg.Id.Value, ownOrg.Id.Value);
        await h.Assignments.SetOrganizationIdsAsync(target.Id.Value, [ownOrg.Id.Value, foreignOrg.Id.Value]);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [
                new UserOrganizationPermission(ownOrg.Id.Value, "teams"),
                new UserOrganizationPermission(foreignOrg.Id.Value, "players"),
            ]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);
        h.Clock.UtcNow = Now.AddHours(1);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, ownOrg.Id.Value) with { TenantIds = [ownOrg.Id.Value] },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid, result.Error);
        await AssertUnchangedAsync(h, target.Id, ownOrg.Id.Value, ownOrg.Id.Value, foreignOrg.Id.Value);
        var permissions = await h.UserPermissions.GetByUserIdAsync(target.Id.Value);
        Assert.Equal(
            new[] { (ownOrg.Id.Value, "teams"), (foreignOrg.Id.Value, "players") },
            permissions.Select(permission => (permission.OrganizationId, permission.ToolKey)).ToArray());
    }

    [Fact]
    public async Task TenantAdmin_ExplicitTenantIds_WithStoredMissingAssignment_FailsWithoutMutation()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var missingOrganizationId = Guid.NewGuid();
        var target = await SeedTargetAsync(h, tenantId, ownOrg.Id.Value, ownOrg.Id.Value);
        await h.Assignments.SetOrganizationIdsAsync(target.Id.Value, [ownOrg.Id.Value, missingOrganizationId]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);
        h.Clock.UtcNow = Now.AddHours(1);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, ownOrg.Id.Value) with { TenantIds = [ownOrg.Id.Value] },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid, result.Error);
        await AssertUnchangedAsync(h, target.Id, ownOrg.Id.Value, ownOrg.Id.Value, missingOrganizationId);
        Assert.Empty(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
    }

    [Fact]
    public async Task TenantAdmin_WithOrphanForeignPermissionRow_FailsWithoutMutation()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, tenantId, ownOrg.Id.Value, ownOrg.Id.Value);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [
                new UserOrganizationPermission(ownOrg.Id.Value, "teams"),
                new UserOrganizationPermission(foreignOrg.Id.Value, "players"),
            ]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);
        h.Clock.UtcNow = Now.AddHours(1);

        var result = await h.Update.Handle(PermissionOnly(target.Id.Value, ownOrg.Id.Value), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid, result.Error);
        await AssertUnchangedAsync(h, target.Id, ownOrg.Id.Value, ownOrg.Id.Value);
        Assert.Equal(2, (await h.UserPermissions.GetByUserIdAsync(target.Id.Value)).Count);
    }

    [Fact]
    public async Task TenantAdmin_ClearingForeignPrimary_WithItsPermissionRows_FailsWithoutMutation()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var target = await SeedTargetAsync(h, tenantId, foreignOrg.Id.Value, ownOrg.Id.Value);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [new UserOrganizationPermission(foreignOrg.Id.Value, "players")]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);
        h.Clock.UtcNow = Now.AddHours(1);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, ownOrg.Id.Value) with { OrganizationId = Guid.Empty },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid, result.Error);
        await AssertUnchangedAsync(h, target.Id, foreignOrg.Id.Value, ownOrg.Id.Value);
        var permission = Assert.Single(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
        Assert.Equal(foreignOrg.Id.Value, permission.OrganizationId);
    }

    [Fact]
    public async Task TenantAdmin_ExplicitTenantIds_ReplacingOwnAssignments_Saves()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "admin@zonarhub.dev", "Admin");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var otherOwnOrg = await h.SeedOrganizationAsync(tenantId, "Other Own Org");
        var target = await SeedTargetAsync(h, tenantId, ownOrg.Id.Value, ownOrg.Id.Value);
        await h.Assignments.SetOrganizationIdsAsync(target.Id.Value, [ownOrg.Id.Value, otherOwnOrg.Id.Value]);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [new UserOrganizationPermission(otherOwnOrg.Id.Value, "players")]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, ownOrg.Id.Value) with { TenantIds = [ownOrg.Id.Value] },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { ownOrg.Id.Value },
            (await h.Assignments.GetOrganizationIdsByUserIdAsync(target.Id.Value)).ToArray());
        var permission = Assert.Single(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
        Assert.Equal((ownOrg.Id.Value, "teams"), (permission.OrganizationId, permission.ToolKey));
    }

    [Fact]
    public async Task SystemAdmin_ExplicitTenantIds_ReplacesForeignAssignmentAndOrphanRows()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var ownOrg = await h.SeedOrganizationAsync(tenantId, "Own Org");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var orphanOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Orphan Org");
        var target = await SeedTargetAsync(h, tenantId, ownOrg.Id.Value, ownOrg.Id.Value);
        await h.Assignments.SetOrganizationIdsAsync(target.Id.Value, [ownOrg.Id.Value, foreignOrg.Id.Value]);
        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [
                new UserOrganizationPermission(foreignOrg.Id.Value, "players"),
                new UserOrganizationPermission(orphanOrg.Id.Value, "players"),
            ]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Update.Handle(
            PermissionOnly(target.Id.Value, ownOrg.Id.Value) with { TenantIds = [ownOrg.Id.Value] },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new[] { ownOrg.Id.Value },
            (await h.Assignments.GetOrganizationIdsByUserIdAsync(target.Id.Value)).ToArray());
        var permission = Assert.Single(await h.UserPermissions.GetByUserIdAsync(target.Id.Value));
        Assert.Equal((ownOrg.Id.Value, "teams"), (permission.OrganizationId, permission.ToolKey));
    }

    private static UpdateAdminUserCommand PermissionOnly(Guid userId, Guid organizationId) =>
        new(
            userId,
            FullName: "Changed Name",
            Phone: null,
            RoleId: null,
            OrganizationId: null,
            TenantIds: null,
            PermissionsByOrganization: [new AdminUserOrganizationPermissionInput(organizationId, ["teams"])],
            IsActive: null);

    private static async Task<User> SeedTargetAsync(
        AdminUsersTestHarness h,
        Guid tenantId,
        Guid primaryOrganizationId,
        Guid assignedOrganizationId)
    {
        var user = await h.SeedUserAsync(UserRole.Viewer, tenantId, $"target-{Guid.NewGuid():N}@zonarhub.dev", "Target");
        Assert.True(user.AssignOrganization(primaryOrganizationId, Now).IsSuccess);
        h.Users.Update(user);
        await h.Assignments.SetOrganizationIdsAsync(user.Id.Value, [assignedOrganizationId]);
        return user;
    }

    private static async Task AssertUnchangedAsync(
        AdminUsersTestHarness h,
        UserId userId,
        Guid primaryOrganizationId,
        params Guid[] assignedOrganizationIds)
    {
        var stored = await h.Users.GetByIdAsync(userId);
        Assert.NotNull(stored);
        Assert.Equal("Target", stored.FullName);
        Assert.Equal(primaryOrganizationId, stored.OrganizationId);
        Assert.Equal(Now, stored.UpdatedAtUtc);
        Assert.Equal(
            assignedOrganizationIds,
            (await h.Assignments.GetOrganizationIdsByUserIdAsync(userId.Value)).ToArray());
    }
}
