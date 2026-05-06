using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminPermissions.GetEffective;

public sealed class GetEffectivePermissionsHandler : IRequestHandler<GetEffectivePermissionsQuery, Result<EffectivePermissionsResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IUserPermissionService _permissionService;

    public GetEffectivePermissionsHandler(
        ICurrentUser currentUser,
        IUserRepository users,
        IUserPermissionService permissionService)
    {
        _currentUser = currentUser;
        _users = users;
        _permissionService = permissionService;
    }

    public async Task<Result<EffectivePermissionsResponse>> Handle(
        GetEffectivePermissionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return Result.Failure<EffectivePermissionsResponse>(AdminPermissionErrors.CallerNotAuthenticated);
        }

        var user = await _users.GetByIdAsync(new UserId(_currentUser.UserId.Value), cancellationToken);
        if (user is null)
        {
            return Result.Failure<EffectivePermissionsResponse>(AdminPermissionErrors.CallerNotFound);
        }

        var organizationId = request.OrganizationId ?? _currentUser.OrganizationId ?? user.OrganizationId;
        var tools = await _permissionService.GetEffectiveToolKeysAsync(user, organizationId, cancellationToken);

        return Result.Success(new EffectivePermissionsResponse(organizationId, tools));
    }
}
