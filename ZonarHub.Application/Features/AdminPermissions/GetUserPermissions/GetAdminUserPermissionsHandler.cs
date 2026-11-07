using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminUsers;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminPermissions.GetUserPermissions;

public sealed class GetAdminUserPermissionsHandler : IRequestHandler<GetAdminUserPermissionsQuery, Result<AdminUserPermissionsResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IUserOrganizationPermissionRepository _permissions;
    private readonly IUserPermissionService _permissionService;
    private readonly IOrganizationRepository _organizations;

    public GetAdminUserPermissionsHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IUserOrganizationPermissionRepository permissions,
        IUserPermissionService permissionService,
        IOrganizationRepository organizations)
    {
        _currentUser = currentUser;
        _users = users;
        _assignments = assignments;
        _permissions = permissions;
        _permissionService = permissionService;
        _organizations = organizations;
    }

    public async Task<Result<AdminUserPermissionsResponse>> Handle(
        GetAdminUserPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return Result.Failure<AdminUserPermissionsResponse>(AdminPermissionErrors.CallerNotAuthenticated);
        }

        var caller = await _users.GetByIdAsync(new UserId(_currentUser.UserId.Value), cancellationToken);
        if (caller is null)
        {
            return Result.Failure<AdminUserPermissionsResponse>(AdminPermissionErrors.CallerNotFound);
        }

        var target = await _users.GetByIdAsync(new UserId(request.UserId), cancellationToken);
        if (target is null)
        {
            return Result.Failure<AdminUserPermissionsResponse>(AdminPermissionErrors.UserNotFound);
        }

        if (!AdminUserAuthorization.CanManageUser(caller, target))
        {
            return Result.Failure<AdminUserPermissionsResponse>(AdminPermissionErrors.Forbidden);
        }

        var organizationIds = (await _assignments.GetOrganizationIdsByUserIdAsync(target.Id.Value, cancellationToken)).ToList();
        if (target.OrganizationId is { } primaryOrganizationId && !organizationIds.Contains(primaryOrganizationId))
        {
            organizationIds.Insert(0, primaryOrganizationId);
        }

        if (!AdminUserAuthorization.IsSystemAdmin(caller))
        {
            organizationIds = await FilterToCallerTenantAsync(organizationIds, caller.TenantId, cancellationToken);
        }

        var persisted = await _permissions.GetByUserIdAsync(target.Id.Value, cancellationToken);
        var groupedPersisted = persisted
            .GroupBy(permission => permission.OrganizationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(permission => permission.ToolKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var defaults = await _permissionService.GetDefaultToolKeysAsync(target.Role, cancellationToken);

        var response = new AdminUserPermissionsResponse(
            target.Id.Value,
            organizationIds
                .Select(organizationId => new UserOrganizationPermissionsResponse(
                    organizationId,
                    groupedPersisted.TryGetValue(organizationId, out var tools) && tools.Count > 0
                        ? tools
                        : defaults))
                .ToList());

        return Result.Success(response);
    }

    /// <summary>
    /// Keeps only organizations that belong to the tenant administrator's own tenant.
    /// Organizations that cannot be resolved are excluded (fail-closed) so anomalous
    /// foreign-tenant assignments never leak to tenant-scoped callers.
    /// </summary>
    private async Task<List<Guid>> FilterToCallerTenantAsync(
        IReadOnlyList<Guid> organizationIds,
        Guid? callerTenantId,
        CancellationToken cancellationToken)
    {
        var visible = new List<Guid>(organizationIds.Count);
        foreach (var organizationId in organizationIds)
        {
            var organization = await _organizations.GetByIdAsync(new OrganizationId(organizationId), cancellationToken);
            if (organization is not null && organization.TenantId == callerTenantId)
            {
                visible.Add(organizationId);
            }
        }

        return visible;
    }
}
