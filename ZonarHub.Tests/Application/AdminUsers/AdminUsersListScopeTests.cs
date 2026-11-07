using System.Net;
using System.Text;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Application.Features.AdminUsers.GetAll;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;
using ZonarHub.Infrastructure.Persistence.Supabase;

namespace ZonarHub.Tests.Application.AdminUsers;

public class AdminUsersListScopeTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

    private static AdminUserFilter Filter(int page = 1, int pageSize = 20) =>
        new(null, null, null, page, pageSize);

    [Fact]
    public async Task SystemAdmin_OrganizationScope_ReturnsOnlyPrimaryOrAssignedMembers()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var otherOrg = await h.SeedOrganizationAsync(tenant, "Org B");

        var primary = await SeedMemberAsync(h, tenant, "primary@zonarhub.dev", primaryOrganizationId: org.Id.Value);
        var assigned = await SeedMemberAsync(h, tenant, "assigned@zonarhub.dev", assignedOrganizationIds: [otherOrg.Id.Value, org.Id.Value]);
        var otherMember = await SeedMemberAsync(h, tenant, "other@zonarhub.dev", primaryOrganizationId: otherOrg.Id.Value);
        var unassigned = await h.SeedUserAsync(UserRole.Viewer, tenant, "unassigned@zonarhub.dev", "Unassigned");

        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(
            new[] { assigned.Id.Value, primary.Id.Value },
            result.Value.Items.Select(item => item.Id).ToArray());
        Assert.DoesNotContain(result.Value.Items, item => item.Id == otherMember.Id.Value);
        Assert.DoesNotContain(result.Value.Items, item => item.Id == unassigned.Id.Value);
        Assert.DoesNotContain(result.Value.Items, item => item.Id == caller.Id.Value);
    }

    [Fact]
    public async Task OrganizationScope_IsTheDefault_WhenScopeIsOmitted()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var member = await SeedMemberAsync(h, tenant, "member@zonarhub.dev", primaryOrganizationId: org.Id.Value);
        await h.SeedUserAsync(UserRole.Viewer, tenant, "outsider@zonarhub.dev", "Outsider");

        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), Scope: null, OrganizationId: org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(member.Id.Value, Assert.Single(result.Value.Items).Id);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("organization")]
    [InlineData(" Organization ")]
    public async Task OrganizationScope_WithoutOrganization_FailsClosed(string? scope)
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "someone@zonarhub.dev", "Someone");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), scope, OrganizationId: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.ListOrganizationRequired.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    public async Task SystemAdmin_AllScope_ReturnsEveryUser(string scope)
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var org = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        await SeedMemberAsync(h, org.TenantId, "member@zonarhub.dev", primaryOrganizationId: org.Id.Value);
        await h.SeedUserAsync(UserRole.Viewer, Guid.NewGuid(), "other-tenant@zonarhub.dev", "Other");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), scope, OrganizationId: null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(3, result.Value.Items.Count);
    }

    [Fact]
    public async Task TenantAdmin_AllScope_IsForbidden()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenant, "admin@zonarhub.dev", "Admin");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.All, OrganizationId: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.Forbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task AllScope_CombinedWithOrganization_IsRejected()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var org = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.All, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.ListScopeInvalid.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("global")]
    [InlineData("")]
    [InlineData("organizations")]
    public async Task UnknownScope_IsRejected(string scope)
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var org = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), scope, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.ListScopeInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task TenantAdmin_OrganizationScope_IncludesMembersAndUnassignedTenantUsersOnly()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var siblingOrg = await h.SeedOrganizationAsync(tenant, "Org B");
        var caller = await SeedMemberAsync(h, tenant, "admin@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.Admin);

        var assigned = await SeedMemberAsync(h, tenant, "assigned@zonarhub.dev", assignedOrganizationIds: [org.Id.Value]);
        var unassigned = await h.SeedUserAsync(UserRole.Viewer, tenant, "unassigned@zonarhub.dev", "Unassigned");
        var siblingMember = await SeedMemberAsync(h, tenant, "sibling@zonarhub.dev", primaryOrganizationId: siblingOrg.Id.Value);
        var otherTenantUnassigned = await h.SeedUserAsync(UserRole.Viewer, otherTenant, "foreign@zonarhub.dev", "Foreign");
        var otherTenantAssigned = await SeedMemberAsync(h, otherTenant, "foreign-assigned@zonarhub.dev", assignedOrganizationIds: [org.Id.Value]);

        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(
            new[] { caller.Id.Value, assigned.Id.Value, unassigned.Id.Value }.OrderBy(id => id).ToArray(),
            result.Value.Items.Select(item => item.Id).OrderBy(id => id).ToArray());
        Assert.DoesNotContain(result.Value.Items, item => item.Id == siblingMember.Id.Value);
        Assert.DoesNotContain(result.Value.Items, item => item.Id == otherTenantUnassigned.Id.Value);
        Assert.DoesNotContain(result.Value.Items, item => item.Id == otherTenantAssigned.Id.Value);
    }

    [Fact]
    public async Task TenantAdmin_OrganizationScope_ExcludesUserAssignedOnlyToAnotherOrganization()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var siblingOrg = await h.SeedOrganizationAsync(tenant, "Org B");
        var caller = await SeedMemberAsync(h, tenant, "admin@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.Admin);
        var assignedElsewhere = await SeedMemberAsync(h, tenant, "elsewhere@zonarhub.dev", assignedOrganizationIds: [siblingOrg.Id.Value]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(assignedElsewhere.OrganizationId);
        Assert.DoesNotContain(result.Value.Items, item => item.Id == assignedElsewhere.Id.Value);
        Assert.Equal(caller.Id.Value, Assert.Single(result.Value.Items).Id);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task SystemAdmin_OrganizationScope_ExcludesUnassignedTenantUsers()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        await h.SeedUserAsync(UserRole.Viewer, tenant, "unassigned@zonarhub.dev", "Unassigned");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task TenantAdmin_ForeignTenantOrganization_IsRejected()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.Admin, Guid.NewGuid(), "admin@zonarhub.dev", "Admin");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        await SeedMemberAsync(h, foreignOrg.TenantId, "foreign@zonarhub.dev", primaryOrganizationId: foreignOrg.Id.Value);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, foreignOrg.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task TenantAdminWithoutTenant_OrganizationScope_IsRejected()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.Admin, tenantId: null, "admin@zonarhub.dev", "Admin");
        var org = await h.SeedOrganizationAsync(Guid.NewGuid(), "Org A");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task MissingOrganization_IsRejected()
    {
        var h = new AdminUsersTestHarness(Now);
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task InactiveOrganization_IsRejected()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.Admin, tenant, "admin@zonarhub.dev", "Admin");
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var deactivate = org.Update(org.DisplayName, org.LegalName, org.Description, org.Type, org.LogoUrl, isActive: false, Now);
        Assert.True(deactivate.IsSuccess);
        h.Organizations.Update(org);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.OrganizationScopeInvalid.Code, result.Error.Code);
    }

    [Fact]
    public async Task OrganizationScope_FiltersBeforePaging_AndCountsOnlyMembers()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var otherOrg = await h.SeedOrganizationAsync(tenant, "Org B");

        // Non-members sort first by email so paging before filtering would return an empty or partial page.
        for (var i = 0; i < 5; i++)
        {
            await SeedMemberAsync(h, tenant, $"a-outsider-{i}@zonarhub.dev", primaryOrganizationId: otherOrg.Id.Value);
        }

        await SeedMemberAsync(h, tenant, "m-1@zonarhub.dev", primaryOrganizationId: org.Id.Value);
        await SeedMemberAsync(h, tenant, "m-2@zonarhub.dev", assignedOrganizationIds: [org.Id.Value]);
        var third = await SeedMemberAsync(h, tenant, "m-3@zonarhub.dev", primaryOrganizationId: org.Id.Value);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(page: 2, pageSize: 2), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(third.Id.Value, Assert.Single(result.Value.Items).Id);
    }

    [Fact]
    public async Task TenantAdmin_OrganizationScope_ExcludesSameTenantSystemAdminsBeforePaging()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var caller = await SeedMemberAsync(h, tenant, "m-admin@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.Admin);

        // System administrators sort first by email so a post-page filter would return a short page.
        await SeedMemberAsync(h, tenant, "a-sys-primary@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.SystemAdmin);
        await SeedMemberAsync(h, tenant, "a-sys-assigned@zonarhub.dev", assignedOrganizationIds: [org.Id.Value], role: UserRole.SystemAdmin);
        var assigned = await SeedMemberAsync(h, tenant, "m-assigned@zonarhub.dev", assignedOrganizationIds: [org.Id.Value]);
        var primary = await SeedMemberAsync(h, tenant, "m-primary@zonarhub.dev", primaryOrganizationId: org.Id.Value);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var firstPage = await h.List.Handle(
            new GetAdminUsersQuery(Filter(page: 1, pageSize: 2), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);
        var secondPage = await h.List.Handle(
            new GetAdminUsersQuery(Filter(page: 2, pageSize: 2), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(firstPage.IsSuccess);
        Assert.True(secondPage.IsSuccess);
        Assert.Equal(3, firstPage.Value.TotalCount);
        Assert.Equal(3, secondPage.Value.TotalCount);
        Assert.Equal(new[] { caller.Id.Value, assigned.Id.Value }, firstPage.Value.Items.Select(item => item.Id).ToArray());
        Assert.Equal(primary.Id.Value, Assert.Single(secondPage.Value.Items).Id);
    }

    [Fact]
    public async Task TenantAdmin_OrganizationScope_SystemAdminRoleFilter_ReturnsEmpty()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var caller = await SeedMemberAsync(h, tenant, "admin@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.Admin);
        await SeedMemberAsync(h, tenant, "sys-primary@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.SystemAdmin);
        await SeedMemberAsync(h, tenant, "sys-assigned@zonarhub.dev", assignedOrganizationIds: [org.Id.Value], role: UserRole.SystemAdmin);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(
                new AdminUserFilter(null, "role001", null, 1, 20),
                AdminUserListScopes.Organization,
                org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task SystemAdmin_OrganizationScope_IncludesSameTenantSystemAdminMembers()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var caller = await SeedMemberAsync(h, tenant, "root@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.SystemAdmin);
        var sysAssigned = await SeedMemberAsync(h, tenant, "sys-assigned@zonarhub.dev", assignedOrganizationIds: [org.Id.Value], role: UserRole.SystemAdmin);
        var member = await SeedMemberAsync(h, tenant, "member@zonarhub.dev", primaryOrganizationId: org.Id.Value);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(
            new[] { member.Id.Value, caller.Id.Value, sysAssigned.Id.Value },
            result.Value.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task TenantAdmin_List_HidesForeignTenantPrimaryOrganization()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var caller = await SeedMemberAsync(h, tenant, "admin@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.Admin);
        var member = await SeedMemberAsync(
            h,
            tenant,
            "member@zonarhub.dev",
            primaryOrganizationId: foreignOrg.Id.Value,
            assignedOrganizationIds: [org.Id.Value]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items, item => item.Id == member.Id.Value);
        Assert.Null(item.OrganizationId);
        Assert.Null(item.OrganizationName);
        Assert.Equal(new[] { org.Id.Value }, item.TenantIds);
        Assert.Equal(new[] { "Org A" }, item.TenantNames);
    }

    [Fact]
    public async Task TenantAdmin_List_HidesForeignTenantAssignments_KeepsInTenantOrganizations()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var siblingOrg = await h.SeedOrganizationAsync(tenant, "Org B");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var caller = await SeedMemberAsync(h, tenant, "admin@zonarhub.dev", primaryOrganizationId: org.Id.Value, role: UserRole.Admin);
        var member = await SeedMemberAsync(
            h,
            tenant,
            "member@zonarhub.dev",
            primaryOrganizationId: org.Id.Value,
            assignedOrganizationIds: [org.Id.Value, foreignOrg.Id.Value, Guid.NewGuid(), siblingOrg.Id.Value]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items, item => item.Id == member.Id.Value);
        Assert.Equal(org.Id.Value, item.OrganizationId);
        Assert.Equal("Org A", item.OrganizationName);
        Assert.Equal(new[] { org.Id.Value, siblingOrg.Id.Value }, item.TenantIds);
        Assert.Equal(new[] { "Org A", "Org B" }, item.TenantNames);
    }

    [Fact]
    public async Task SystemAdmin_List_KeepsForeignTenantOrganizationMetadata()
    {
        var h = new AdminUsersTestHarness(Now);
        var tenant = Guid.NewGuid();
        var org = await h.SeedOrganizationAsync(tenant, "Org A");
        var foreignOrg = await h.SeedOrganizationAsync(Guid.NewGuid(), "Foreign Org");
        var caller = await h.SeedUserAsync(UserRole.SystemAdmin, tenantId: null, "root@zonarhub.dev", "Root");
        var member = await SeedMemberAsync(
            h,
            tenant,
            "member@zonarhub.dev",
            primaryOrganizationId: foreignOrg.Id.Value,
            assignedOrganizationIds: [org.Id.Value, foreignOrg.Id.Value]);
        h.CurrentUser.Authenticate(caller.Id.Value, caller.Email);

        var result = await h.List.Handle(
            new GetAdminUsersQuery(Filter(), AdminUserListScopes.Organization, org.Id.Value),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(member.Id.Value, item.Id);
        Assert.Equal(foreignOrg.Id.Value, item.OrganizationId);
        Assert.Equal("Foreign Org", item.OrganizationName);
        Assert.Equal(new[] { org.Id.Value, foreignOrg.Id.Value }, item.TenantIds);
        Assert.Equal(new[] { "Org A", "Foreign Org" }, item.TenantNames);
    }

    [Fact]
    public async Task SupabaseUserRepository_WithoutMembership_KeepsTenantQuery()
    {
        var tenant = Guid.NewGuid();
        string? requestedQuery = null;
        var repository = CreateSupabaseRepository(request =>
        {
            requestedQuery = Uri.UnescapeDataString(request.RequestUri!.Query);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
            response.Content.Headers.TryAddWithoutValidation("Content-Range", "*/0");
            return response;
        });

        var (items, total) = await repository.ListAsync(new UserQuery(tenant, null, null, true, 1, 20));

        Assert.Empty(items);
        Assert.Equal(0, total);
        Assert.Contains($"tenant_id=eq.{tenant}", requestedQuery);
    }

    private static async Task<User> SeedMemberAsync(
        AdminUsersTestHarness h,
        Guid tenantId,
        string email,
        Guid? primaryOrganizationId = null,
        Guid[]? assignedOrganizationIds = null,
        UserRole role = UserRole.Viewer)
    {
        var user = await h.SeedUserAsync(role, tenantId, email, email);
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

    private static IUserRepository CreateSupabaseRepository(Func<HttpRequestMessage, HttpResponseMessage> send)
    {
        var client = new HttpClient(new Handler(send)) { BaseAddress = new Uri("https://example.invalid") };
        return new UserRepository(new Factory(client), new SupabaseOperationContext());
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}
