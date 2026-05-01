using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminUsers.Update;

public sealed class UpdateAdminUserHandler : IRequestHandler<UpdateAdminUserCommand, Result<AdminUserResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IOrganizationRepository _organizations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdateAdminUserHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IUserOrganizationAssignmentRepository assignments,
        IOrganizationRepository organizations,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _currentUser = currentUser;
        _users = users;
        _assignments = assignments;
        _organizations = organizations;
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

        var organizationScopeValidation = await ValidateOrganizationsScopeAsync(caller, organizationIds, cancellationToken);
        if (organizationScopeValidation.IsFailure)
        {
            return Result.Failure<AdminUserResponse>(organizationScopeValidation.Error);
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
}
