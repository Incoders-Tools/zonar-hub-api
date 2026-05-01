using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers.Create;

public sealed class CreateAdminUserHandler : IRequestHandler<CreateAdminUserCommand, Result<AdminUserResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IOrganizationRepository _organizations;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateAdminUserHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IOrganizationRepository organizations,
        IPasswordHasher hasher,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _currentUser = currentUser;
        _users = users;
        _assignments = assignments;
        _organizations = organizations;
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
}
