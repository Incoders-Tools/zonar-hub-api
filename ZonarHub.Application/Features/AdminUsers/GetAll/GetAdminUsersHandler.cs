using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Common.Pagination;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers.GetAll;

public sealed class GetAdminUsersHandler
    : IRequestHandler<GetAdminUsersQuery, Result<PageResult<AdminUserResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IOrganizationRepository _organizations;

    public GetAdminUsersHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IOrganizationRepository organizations)
    {
        _currentUser = currentUser;
        _users = users;
        _assignments = assignments;
        _organizations = organizations;
    }

    public async Task<Result<PageResult<AdminUserResponse>>> Handle(
        GetAdminUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return Result.Failure<PageResult<AdminUserResponse>>(AdminUserErrors.CallerNotAuthenticated);
        }

        var caller = await _users.GetByIdAsync(new UserId(_currentUser.UserId.Value), cancellationToken);
        if (caller is null)
        {
            return Result.Failure<PageResult<AdminUserResponse>>(AdminUserErrors.CallerNotFound);
        }

        var filter = Normalize(request.Filter);
        UserRole? role = null;
        if (!string.IsNullOrWhiteSpace(filter.RoleId))
        {
            if (!AdminUserRoleMapper.TryFromRoleId(filter.RoleId, out var mappedRole))
            {
                return Result.Failure<PageResult<AdminUserResponse>>(AdminUserErrors.RoleIdInvalid);
            }

            role = mappedRole;
        }

        var scope = await ResolveScopeAsync(caller, request, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure<PageResult<AdminUserResponse>>(scope.Error);
        }

        var query = new UserQuery(
            AdminUserAuthorization.IsSystemAdmin(caller) ? null : caller.TenantId,
            filter.Search,
            role,
            filter.IsActive,
            filter.Page,
            filter.PageSize,
            scope.Value);

        var (items, totalCount) = await _users.ListAsync(query, cancellationToken);

        var userIds = items.Select(item => item.Id.Value).ToList();
        var tenantIdsByUser = await _assignments.GetOrganizationIdsByUserIdsAsync(userIds, cancellationToken);

        var organizationIds = new HashSet<Guid>();
        foreach (var user in items)
        {
            if (user.OrganizationId is { } primaryId)
            {
                organizationIds.Add(primaryId);
            }

            if (tenantIdsByUser.TryGetValue(user.Id.Value, out var assignedIds))
            {
                foreach (var assignedId in assignedIds)
                {
                    organizationIds.Add(assignedId);
                }
            }
        }

        var organizationNames = new Dictionary<Guid, string>();
        foreach (var organizationId in organizationIds)
        {
            var organization = await _organizations.GetByIdAsync(new ZonarHub.Domain.Organizations.OrganizationId(organizationId), cancellationToken);
            if (organization is not null)
            {
                organizationNames[organizationId] = organization.DisplayName;
            }
        }

        var mapped = items
            .Select(user =>
            {
                if (!tenantIdsByUser.TryGetValue(user.Id.Value, out var ids) || ids.Count == 0)
                {
                    ids = user.OrganizationId is { } organizationId
                        ? new[] { organizationId }
                        : Array.Empty<Guid>();
                }

                return AdminUserResponse.FromDomain(user, ids, organizationNames);
            })
            .ToList();

        return Result.Success(new PageResult<AdminUserResponse>(mapped, filter.Page, filter.PageSize, totalCount));
    }

    /// <summary>
    /// Resolves the requested list scope into a membership filter (<c>null</c> means all users).
    /// Organization scope is the default and fails closed without a valid, active, in-tenant organization;
    /// all scope is reserved for system administrators. Tenant administrators additionally see
    /// unassigned users of their tenant so they can assign them.
    /// </summary>
    private async Task<Result<UserOrganizationMembership?>> ResolveScopeAsync(
        User caller,
        GetAdminUsersQuery request,
        CancellationToken cancellationToken)
    {
        var scope = request.Scope?.Trim() ?? AdminUserListScopes.Organization;
        var isSystemAdmin = AdminUserAuthorization.IsSystemAdmin(caller);

        if (string.Equals(scope, AdminUserListScopes.All, StringComparison.OrdinalIgnoreCase))
        {
            if (request.OrganizationId is not null)
            {
                return Result.Failure<UserOrganizationMembership?>(AdminUserErrors.ListScopeInvalid);
            }

            return isSystemAdmin
                ? Result.Success<UserOrganizationMembership?>(null)
                : Result.Failure<UserOrganizationMembership?>(AdminUserErrors.Forbidden);
        }

        if (!string.Equals(scope, AdminUserListScopes.Organization, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<UserOrganizationMembership?>(AdminUserErrors.ListScopeInvalid);
        }

        if (request.OrganizationId is not { } organizationId)
        {
            return Result.Failure<UserOrganizationMembership?>(AdminUserErrors.ListOrganizationRequired);
        }

        var organization = await _organizations.GetByIdAsync(
            new ZonarHub.Domain.Organizations.OrganizationId(organizationId),
            cancellationToken);
        var belongsToCaller = isSystemAdmin ||
            (caller.TenantId is { } callerTenantId && organization?.TenantId == callerTenantId);

        if (organization is null || !organization.IsActive || !belongsToCaller)
        {
            return Result.Failure<UserOrganizationMembership?>(AdminUserErrors.OrganizationScopeInvalid);
        }

        return Result.Success<UserOrganizationMembership?>(
            new UserOrganizationMembership(organizationId, isSystemAdmin ? null : caller.TenantId));
    }

    private static AdminUserFilter Normalize(AdminUserFilter filter)
    {
        var page = filter.Page < 1 ? PageRequest.DefaultPage : filter.Page;
        var pageSize = filter.PageSize switch
        {
            < 1 => PageRequest.DefaultPageSize,
            > PageRequest.MaxPageSize => PageRequest.MaxPageSize,
            _ => filter.PageSize,
        };

        return filter with
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim(),
            RoleId = string.IsNullOrWhiteSpace(filter.RoleId) ? null : filter.RoleId.Trim()
        };
    }
}
