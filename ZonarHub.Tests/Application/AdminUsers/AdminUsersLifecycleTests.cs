using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Application.Features.AdminUsers.Create;
using ZonarHub.Application.Features.AdminUsers.Delete;
using ZonarHub.Application.Features.AdminUsers.GetAll;
using ZonarHub.Application.Features.AdminUsers.Update;
using ZonarHub.Domain.Users;

namespace ZonarHub.Tests.Application.AdminUsers;

public class AdminUsersLifecycleTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_PersistsUserAndAssignments()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root User");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var orgA = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org A", caller.Id.Value);
        var orgB = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org B", caller.Id.Value);

        var result = await h.Create.Handle(
            new CreateAdminUserCommand(
                "admin@zonarhub.dev",
                "New Admin",
                "+34123456789",
                "role002",
                orgA.Id.Value,
                new[] { orgA.Id.Value, orgB.Id.Value },
                PermissionsByOrganization: null,
                "Secret-123"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("admin@zonarhub.dev", result.Value.Email);
        Assert.Equal(orgA.Id.Value, result.Value.OrganizationId);
        Assert.Equal(2, result.Value.TenantIds.Count);

        var saved = await h.Users.GetByIdAsync(new UserId(result.Value.Id), CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(UserRole.Admin, saved.Role);

        var assignmentIds = await h.Assignments.GetOrganizationIdsByUserIdAsync(result.Value.Id, CancellationToken.None);
        Assert.Equal(2, assignmentIds.Count);
        Assert.Contains(orgA.Id.Value, assignmentIds);
        Assert.Contains(orgB.Id.Value, assignmentIds);
    }

    [Fact]
    public async Task Create_WhenCallerNotAuthenticated_ReturnsFailure()
    {
        var h = new AdminUsersTestHarness(Now);

        var result = await h.Create.Handle(
            new CreateAdminUserCommand(
                "admin@zonarhub.dev",
                "New Admin",
                null,
                "role002",
                null,
                null,
                PermissionsByOrganization: null,
                null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.CallerNotAuthenticated.Code, result.Error.Code);
    }

    [Fact]
    public async Task Create_WithPermissionMatrix_PersistsOrganizationToolPermissions()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root User");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var orgA = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org A", caller.Id.Value);
        var orgB = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org B", caller.Id.Value);

        var result = await h.Create.Handle(
            new CreateAdminUserCommand(
                "matrix-admin@zonarhub.dev",
                "Matrix Admin",
                null,
                "role002",
                orgA.Id.Value,
                new[] { orgA.Id.Value, orgB.Id.Value },
                [
                    new AdminUserOrganizationPermissionInput(orgA.Id.Value, ["dashboard", "users"]),
                    new AdminUserOrganizationPermissionInput(orgB.Id.Value, ["dashboard", "complexes"])
                ],
                Password: "Secret-123"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedPermissions = await h.UserPermissions.GetByUserIdAsync(result.Value.Id, CancellationToken.None);
        Assert.Contains(savedPermissions, p => p.OrganizationId == orgA.Id.Value && p.ToolKey == "users");
        Assert.Contains(savedPermissions, p => p.OrganizationId == orgB.Id.Value && p.ToolKey == "complexes");
        Assert.DoesNotContain(savedPermissions, p => p.OrganizationId == orgB.Id.Value && p.ToolKey == "users");
    }

    [Fact]
    public async Task Create_WhenRestrictedToolAssignedToNonSystemRole_ReturnsValidationError()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root User");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var org = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org A", caller.Id.Value);

        var result = await h.Create.Handle(
            new CreateAdminUserCommand(
                "restricted@zonarhub.dev",
                "Restricted Admin",
                null,
                "role002",
                org.Id.Value,
                new[] { org.Id.Value },
                [new AdminUserOrganizationPermissionInput(org.Id.Value, ["dashboard", "roles"])],
                Password: "Secret-123"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.RestrictedToolRoleInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task Update_WhenAdminEscalatesRole_ReturnsForbidden()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();

        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "caller@zonarhub.dev", "Caller");
        var target = await h.SeedUserAsync(UserRole.Viewer, tenantId, "target@zonarhub.dev", "Target");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Update.Handle(
            new UpdateAdminUserCommand(
                target.Id.Value,
                FullName: null,
                Phone: null,
                RoleId: "role001",
                OrganizationId: null,
                TenantIds: null,
                PermissionsByOrganization: null,
                IsActive: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.RoleEscalationForbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task Delete_RemovesUserAndOrganizationAssignments()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root User");
        var target = await h.SeedUserAsync(UserRole.Admin, Guid.NewGuid(), "target@zonarhub.dev", "Target User");

        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        await h.Assignments.SetOrganizationIdsAsync(
            target.Id.Value,
            new[] { orgA, orgB },
            CancellationToken.None);

        await h.UserPermissions.SetPermissionsAsync(
            target.Id.Value,
            [
                new ZonarHub.Application.Abstractions.UserOrganizationPermission(orgA, "dashboard"),
                new ZonarHub.Application.Abstractions.UserOrganizationPermission(orgB, "users")
            ],
            CancellationToken.None);

        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Delete.Handle(new DeleteAdminUserCommand(target.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var deletedUser = await h.Users.GetByIdAsync(target.Id, CancellationToken.None);
        Assert.Null(deletedUser);

        var assignmentIds = await h.Assignments.GetOrganizationIdsByUserIdAsync(target.Id.Value, CancellationToken.None);
        Assert.Empty(assignmentIds);

        var permissions = await h.UserPermissions.GetByUserIdAsync(target.Id.Value, CancellationToken.None);
        Assert.Empty(permissions);
    }

    [Fact]
    public async Task Update_WhenCallerLacksUsersTool_ReturnsForbidden()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantId = Guid.NewGuid();

        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId, "caller@zonarhub.dev", "Caller");
        var target = await h.SeedUserAsync(UserRole.Viewer, tenantId, "target@zonarhub.dev", "Target");
        var org = await h.SeedOrganizationAsync(tenantId, "Org A", caller.Id.Value);

        await h.Assignments.SetOrganizationIdsAsync(caller.Id.Value, [org.Id.Value], CancellationToken.None);
        await h.UserPermissions.SetPermissionsAsync(
            caller.Id.Value,
            [new ZonarHub.Application.Abstractions.UserOrganizationPermission(org.Id.Value, "dashboard")],
            CancellationToken.None);

        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email, org.Id.Value);

        var result = await h.Update.Handle(
            new UpdateAdminUserCommand(
                target.Id.Value,
                FullName: "Target Updated",
                Phone: null,
                RoleId: null,
                OrganizationId: null,
                TenantIds: null,
                PermissionsByOrganization: null,
                IsActive: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.UserToolPermissionForbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task List_ForAdminCaller_IsTenantScoped()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var caller = await h.SeedUserAsync(UserRole.Admin, tenantA, "admin-a@zonarhub.dev", "Admin A");
        var sameTenantUser = await h.SeedUserAsync(UserRole.Viewer, tenantA, "viewer-a@zonarhub.dev", "Viewer A");
        var otherTenantUser = await h.SeedUserAsync(UserRole.Admin, tenantB, "admin-b@zonarhub.dev", "Admin B");

        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(new AdminUserFilter(null, null, null, Page: 1, PageSize: 20)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value.Items, item => item.Id == caller.Id.Value);
        Assert.Contains(result.Value.Items, item => item.Id == sameTenantUser.Id.Value);
        Assert.DoesNotContain(result.Value.Items, item => item.Id == otherTenantUser.Id.Value);
    }

    [Fact]
    public async Task Create_WithEditorRole_PersistsEditorRole()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root User");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.Create.Handle(
            new CreateAdminUserCommand(
                "editor@zonarhub.dev",
                "Circuit Editor",
                "+34123456789",
                "role004",
                OrganizationId: null,
                TenantIds: null,
                PermissionsByOrganization: null,
                Password: "Secret-123"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("role004", result.Value.RoleId);
        Assert.Equal("editor", result.Value.RoleName);

        var saved = await h.Users.GetByIdAsync(new UserId(result.Value.Id), CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(UserRole.Editor, saved!.Role);
    }
}
