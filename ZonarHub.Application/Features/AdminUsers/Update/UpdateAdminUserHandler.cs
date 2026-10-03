using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers.Update;

public sealed class UpdateAdminUserHandler : IRequestHandler<UpdateAdminUserCommand, Result<AdminUserResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IUserOrganizationPermissionRepository _userPermissions;
    private readonly IOrganizationRepository _organizations;
    private readonly ISystemPermissionCatalogRepository _permissionCatalog;
    private readonly IUserPermissionService _permissionService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateAdminUserHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IUserOrganizationPermissionRepository userPermissions,
        IOrganizationRepository organizations,
        ISystemPermissionCatalogRepository permissionCatalog,
        IUserPermissionService permissionService,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _currentUser = currentUser;
        _users = users;
        _assignments = assignments;
        _userPermissions = userPermissions;
        _organizations = organizations;
        _permissionCatalog = permissionCatalog;
        _permissionService = permissionService;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<AdminUserResponse>> Handle(UpdateAdminUserCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return Result.Failure<AdminUserResponse>(AdminUserErrors.CallerNotAuthenticated);
        }

        var caller = await _users.GetByIdAsync(new UserId(_currentUser.UserId.Value), cancellationToken);
        if (caller is null)
        {
            return Result.Failure<AdminUserResponse>(AdminUserErrors.CallerNotFound);
        }

        var user = await _users.GetByIdAsync(new UserId(request.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure<AdminUserResponse>(AdminUserErrors.UserNotFound);
        }

        if (!AdminUserAuthorization.CanManageUser(caller, user))
        {
            return Result.Failure<AdminUserResponse>(AdminUserErrors.Forbidden);
        }

        if (!AdminUserAuthorization.IsSystemAdmin(caller))
        {
            var callerOrganizationId = _currentUser.OrganizationId ?? caller.OrganizationId;
            var canManageUsers = await _permissionService.HasToolAsync(
                caller,
                callerOrganizationId,
                SystemToolKeys.Users,
                cancellationToken);

            if (!canManageUsers)
            {
                return Result.Failure<AdminUserResponse>(AdminUserErrors.UserToolPermissionForbidden);
            }
        }

        var nextRole = user.Role;
        if (!string.IsNullOrWhiteSpace(request.RoleId))
        {
            if (!AdminUserRoleMapper.TryFromRoleId(request.RoleId, out nextRole))
            {
                return Result.Failure<AdminUserResponse>(AdminUserErrors.RoleIdInvalid);
            }

            if (!AdminUserAuthorization.CanAssignRole(caller, nextRole))
            {
                return Result.Failure<AdminUserResponse>(AdminUserErrors.RoleEscalationForbidden);
            }
        }

        var organizationIds = request.TenantIds?.Where(id => id != Guid.Empty).Distinct().ToList() ??
            (await _assignments.GetOrganizationIdsByUserIdAsync(user.Id.Value, cancellationToken)).ToList();

        if (request.OrganizationId is { } primaryRequested && primaryRequested != Guid.Empty)
        {
            if (!organizationIds.Contains(primaryRequested))
            {
                organizationIds.Insert(0, primaryRequested);
            }
        }

        var nextOrganizationId = request.OrganizationId ?? user.OrganizationId ?? organizationIds.FirstOrDefault();

        // Tenant admins never see a foreign or missing primary in the permission matrix, and permission
        // rows are replaced wholesale, so the retained primary must also be in scope to avoid silently
        // dropping its hidden rows. Requesting a same-tenant primary repairs the anomaly.
        var scopedOrganizationIds = AdminUserAuthorization.IsSystemAdmin(caller) ||
            nextOrganizationId == Guid.Empty ||
            organizationIds.Contains(nextOrganizationId)
                ? organizationIds
                : [.. organizationIds, nextOrganizationId];

        var organizationScopeValidation = await ValidateOrganizationsScopeAsync(caller, scopedOrganizationIds, cancellationToken);
        if (organizationScopeValidation.IsFailure)
        {
            return Result.Failure<AdminUserResponse>(organizationScopeValidation.Error);
        }

        var storedScopeValidation = await ValidateStoredOrganizationsScopeAsync(caller, user, cancellationToken);
        if (storedScopeValidation.IsFailure)
        {
            return Result.Failure<AdminUserResponse>(storedScopeValidation.Error);
        }

        var normalizedPermissions = NormalizePermissions(request.PermissionsByOrganization);
        if (normalizedPermissions.Count == 0)
        {
            normalizedPermissions = await BuildPermissionsForUpdateAsync(
                user,
                nextRole,
                organizationIds,
                cancellationToken);
        }

        var permissionValidation = await ValidatePermissionAssignmentsAsync(
            caller,
            nextRole,
            organizationIds,
            normalizedPermissions,
            cancellationToken);

        if (permissionValidation.IsFailure)
        {
            return Result.Failure<AdminUserResponse>(permissionValidation.Error);
        }

        var updateResult = user.UpdateAdminProfile(
            request.FullName ?? user.FullName,
            request.Phone ?? user.Phone,
            nextRole,
            request.IsActive ?? user.IsActive,
            nextOrganizationId == Guid.Empty ? null : nextOrganizationId,
            _clock.UtcNow);

        if (updateResult.IsFailure)
        {
            return Result.Failure<AdminUserResponse>(updateResult.Error);
        }

        _users.Update(user);
        await _assignments.SetOrganizationIdsAsync(user.Id.Value, organizationIds, cancellationToken);
        await _userPermissions.SetPermissionsAsync(
            user.Id.Value,
            ToUserOrganizationPermissions(normalizedPermissions),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var organizationNames = await ResolveOrganizationNamesAsync(organizationIds, user.OrganizationId, cancellationToken);
        return Result.Success(AdminUserResponse.FromDomain(user, organizationIds, organizationNames));
    }

    private async Task<Result> ValidateOrganizationsScopeAsync(User caller, IReadOnlyList<Guid> organizationIds, CancellationToken cancellationToken)
    {
        if (organizationIds.Count == 0)
        {
            return Result.Success();
        }

        foreach (var organizationId in organizationIds)
        {
            var organization = await _organizations.GetByIdAsync(new ZonarHub.Domain.Organizations.OrganizationId(organizationId), cancellationToken);
            if (organization is null)
            {
                return Result.Failure(AdminUserErrors.OrganizationScopeInvalid);
            }

            if (!AdminUserAuthorization.IsSystemAdmin(caller) && caller.TenantId != organization.TenantId)
            {
                return Result.Failure(AdminUserErrors.OrganizationScopeInvalid);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Assignments and permission rows are replaced wholesale, so for tenant admins every stored assignment and
    /// permission organization must also be in scope; otherwise hidden foreign rows would be silently deleted.
    /// No exemption applies to an explicitly replaced primary: a foreign primary can only be repaired when it has
    /// no stored assignment or permission rows, so a repair never deletes cross-tenant data.
    /// </summary>
    private async Task<Result> ValidateStoredOrganizationsScopeAsync(
        User caller,
        User user,
        CancellationToken cancellationToken)
    {
        if (AdminUserAuthorization.IsSystemAdmin(caller))
        {
            return Result.Success();
        }

        var storedAssignmentIds = await _assignments.GetOrganizationIdsByUserIdAsync(user.Id.Value, cancellationToken);
        var storedPermissions = await _userPermissions.GetByUserIdAsync(user.Id.Value, cancellationToken);
        var storedOrganizationIds = storedAssignmentIds
            .Concat(storedPermissions.Select(permission => permission.OrganizationId))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        return await ValidateOrganizationsScopeAsync(caller, storedOrganizationIds, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveOrganizationNamesAsync(
        IReadOnlyList<Guid> organizationIds,
        Guid? primaryOrganizationId,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>(organizationIds);
        if (primaryOrganizationId is { } primaryId)
        {
            ids.Add(primaryId);
        }

        var names = new Dictionary<Guid, string>();
        foreach (var id in ids)
        {
            var organization = await _organizations.GetByIdAsync(new ZonarHub.Domain.Organizations.OrganizationId(id), cancellationToken);
            if (organization is not null)
            {
                names[id] = organization.DisplayName;
            }
        }

        return names;
    }

    private static IReadOnlyList<AdminUserOrganizationPermissionInput> NormalizePermissions(
        IReadOnlyList<AdminUserOrganizationPermissionInput>? requested)
    {
        if (requested is null || requested.Count == 0)
        {
            return [];
        }

        var byOrganization = new Dictionary<Guid, HashSet<string>>();

        foreach (var permission in requested)
        {
            if (permission.OrganizationId == Guid.Empty)
            {
                continue;
            }

            if (!byOrganization.TryGetValue(permission.OrganizationId, out var toolSet))
            {
                toolSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byOrganization[permission.OrganizationId] = toolSet;
            }

            foreach (var key in permission.ToolKeys)
            {
                var normalized = key?.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    toolSet.Add(normalized);
                }
            }
        }

        return byOrganization
            .Select(entry => new AdminUserOrganizationPermissionInput(entry.Key, entry.Value.ToList()))
            .ToList();
    }

    private async Task<IReadOnlyList<AdminUserOrganizationPermissionInput>> BuildPermissionsForUpdateAsync(
        User user,
        UserRole targetRole,
        IReadOnlyList<Guid> organizationIds,
        CancellationToken cancellationToken)
    {
        if (organizationIds.Count == 0)
        {
            return [];
        }

        var persisted = await _userPermissions.GetByUserIdAsync(user.Id.Value, cancellationToken);
        var groupedPersisted = persisted
            .GroupBy(permission => permission.OrganizationId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(permission => permission.ToolKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var defaults = await _permissionService.GetDefaultToolKeysAsync(targetRole, cancellationToken);

        return organizationIds
            .Select(organizationId => new AdminUserOrganizationPermissionInput(
                organizationId,
                groupedPersisted.TryGetValue(organizationId, out var existing) && existing.Count > 0
                    ? existing
                    : defaults))
            .ToList();
    }

    private async Task<Result> ValidatePermissionAssignmentsAsync(
        User caller,
        UserRole targetRole,
        IReadOnlyList<Guid> organizationIds,
        IReadOnlyList<AdminUserOrganizationPermissionInput> permissionsByOrganization,
        CancellationToken cancellationToken)
    {
        if (permissionsByOrganization.Count == 0)
        {
            return Result.Success();
        }

        var validOrganizationIds = new HashSet<Guid>(organizationIds);
        if (permissionsByOrganization.Any(permission => !validOrganizationIds.Contains(permission.OrganizationId)))
        {
            return Result.Failure(AdminUserErrors.PermissionOrganizationScopeInvalid);
        }

        var requestedToolKeys = permissionsByOrganization
            .SelectMany(permission => permission.ToolKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedToolKeys.Count == 0)
        {
            return Result.Success();
        }

        var tools = await _permissionCatalog.ListToolsByKeysAsync(requestedToolKeys, cancellationToken);
        if (tools.Count != requestedToolKeys.Count || tools.Any(tool => !tool.IsActive))
        {
            return Result.Failure(AdminUserErrors.PermissionToolInvalid);
        }

        var restrictedToolsRequested = tools.Any(tool => tool.IsSystemAdminOnly);
        if (restrictedToolsRequested && targetRole != UserRole.SystemAdmin)
        {
            return Result.Failure(AdminUserErrors.RestrictedToolRoleInvalid);
        }

        if (restrictedToolsRequested && !AdminUserAuthorization.IsSystemAdmin(caller))
        {
            return Result.Failure(AdminUserErrors.RestrictedToolAssignmentForbidden);
        }

        return Result.Success();
    }

    private static IReadOnlyList<UserOrganizationPermission> ToUserOrganizationPermissions(
        IReadOnlyList<AdminUserOrganizationPermissionInput> permissionsByOrganization)
    {
        return permissionsByOrganization
            .SelectMany(permission => permission.ToolKeys.Select(toolKey =>
                new UserOrganizationPermission(permission.OrganizationId, toolKey)))
            .ToList();
    }
}
