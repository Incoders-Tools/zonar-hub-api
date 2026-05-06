using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminPermissions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers.Create;

public sealed class CreateAdminUserHandler : IRequestHandler<CreateAdminUserCommand, Result<AdminUserResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IUserOrganizationPermissionRepository _userPermissions;
    private readonly IOrganizationRepository _organizations;
    private readonly ISystemPermissionCatalogRepository _permissionCatalog;
    private readonly IUserPermissionService _permissionService;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateAdminUserHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IUserOrganizationPermissionRepository userPermissions,
        IOrganizationRepository organizations,
        ISystemPermissionCatalogRepository permissionCatalog,
        IUserPermissionService permissionService,
        IPasswordHasher hasher,
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
        _hasher = hasher;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<AdminUserResponse>> Handle(CreateAdminUserCommand request, CancellationToken cancellationToken)
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

        if (!AdminUserRoleMapper.TryFromRoleId(request.RoleId, out var role))
        {
            return Result.Failure<AdminUserResponse>(AdminUserErrors.RoleIdInvalid);
        }

        if (!AdminUserAuthorization.CanAssignRole(caller, role))
        {
            return Result.Failure<AdminUserResponse>(AdminUserErrors.RoleEscalationForbidden);
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

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.ExistsByEmailAsync(email, cancellationToken))
        {
            return Result.Failure<AdminUserResponse>(UserErrors.EmailAlreadyExists);
        }

        var tenantId = caller.TenantId;
        if (!AdminUserAuthorization.IsSystemAdmin(caller) && tenantId is null)
        {
            return Result.Failure<AdminUserResponse>(AdminUserErrors.Forbidden);
        }

        var organizationIds = NormalizeOrganizationIds(request.TenantIds, request.OrganizationId);
        var organizationScopeValidation = await ValidateOrganizationsScopeAsync(caller, organizationIds, cancellationToken);
        if (organizationScopeValidation.IsFailure)
        {
            return Result.Failure<AdminUserResponse>(organizationScopeValidation.Error);
        }

        var normalizedPermissions = NormalizePermissions(request.PermissionsByOrganization);
        if (normalizedPermissions.Count == 0 && organizationIds.Count > 0)
        {
            var defaults = await _permissionService.GetDefaultToolKeysAsync(role, cancellationToken);
            normalizedPermissions = organizationIds
                .Select(organizationId => new AdminUserOrganizationPermissionInput(organizationId, defaults))
                .ToList();
        }

        var permissionValidation = await ValidatePermissionAssignmentsAsync(
            caller,
            role,
            organizationIds,
            normalizedPermissions,
            cancellationToken);

        if (permissionValidation.IsFailure)
        {
            return Result.Failure<AdminUserResponse>(permissionValidation.Error);
        }

        var password = string.IsNullOrWhiteSpace(request.Password)
            ? $"Tmp-{Guid.NewGuid():N}!"
            : request.Password;

        var register = User.Register(
            UserId.New(),
            email,
            request.FullName,
            request.Phone,
            birthDate: null,
            _hasher.Hash(password),
            role,
            tenantId,
            _clock.UtcNow);

        if (register.IsFailure)
        {
            return Result.Failure<AdminUserResponse>(register.Error);
        }

        var user = register.Value;

        Guid? primaryOrganizationId = request.OrganizationId
            ?? (organizationIds.Count > 0 ? organizationIds[0] : null);

        if (primaryOrganizationId.HasValue)
        {
            var assignResult = user.AssignOrganization(primaryOrganizationId.Value, _clock.UtcNow);
            if (assignResult.IsFailure)
            {
                return Result.Failure<AdminUserResponse>(assignResult.Error);
            }
        }

        await _users.AddAsync(user, cancellationToken);
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

    private static IReadOnlyList<Guid> NormalizeOrganizationIds(IReadOnlyList<Guid>? requestedIds, Guid? primaryId)
    {
        var normalized = new List<Guid>();

        if (requestedIds is not null)
        {
            normalized.AddRange(requestedIds.Where(id => id != Guid.Empty));
        }

        if (primaryId is { } id && id != Guid.Empty)
        {
            normalized.Add(id);
        }

        return normalized.Distinct().ToList();
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
