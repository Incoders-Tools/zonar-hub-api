using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.UserPreferences.SetPrimaryOrganization;

public sealed class SetPrimaryOrganizationHandler : IRequestHandler<SetPrimaryOrganizationCommand, Result>
{
    private static readonly Error Unauthorized = Error.Validation("PrimaryOrganization.Unauthorized", "primaryOrganization.unauthorized");
    private static readonly Error Forbidden = Error.Validation("PrimaryOrganization.Forbidden", "primaryOrganization.forbidden");
    private static readonly Error Ineligible = Error.Validation("PrimaryOrganization.Ineligible", "primaryOrganization.ineligible");
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IOrganizationRepository _organizations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public SetPrimaryOrganizationHandler(ICurrentUser currentUser, IUserRepository users,
        IUserOrganizationAssignmentRepository assignments, IOrganizationRepository organizations,
        IUnitOfWork unitOfWork, IClock clock)
    {
        _currentUser = currentUser;
        _users = users;
        _assignments = assignments;
        _organizations = organizations;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(SetPrimaryOrganizationCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result.Failure(Unauthorized);

        var user = await _users.GetByIdAsync(new UserId(userId), cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure(Unauthorized);

        if (user.Role is not (UserRole.Admin or UserRole.SystemAdmin))
            return Result.Failure(Forbidden);

        if (request.OrganizationId == Guid.Empty)
            return Result.Failure(Ineligible);

        var assigned = await _assignments.GetOrganizationIdsByUserIdAsync(userId, cancellationToken);
        if (!assigned.Contains(request.OrganizationId))
            return Result.Failure(Ineligible);

        var organization = await _organizations.GetByIdAsync(new OrganizationId(request.OrganizationId), cancellationToken);
        if (organization is null || !organization.IsActive ||
            (user.Role != UserRole.SystemAdmin && organization.TenantId != user.TenantId))
            return Result.Failure(Ineligible);

        if (user.OrganizationId == request.OrganizationId)
            return Result.Success();

        var assignedPrimary = user.AssignOrganization(request.OrganizationId, _clock.UtcNow);
        if (assignedPrimary.IsFailure)
            return assignedPrimary;

        _users.Update(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
