using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.GetById;

public sealed class GetAdminRoleByIdHandler : IRequestHandler<GetAdminRoleByIdQuery, Result<AdminRoleResponse>>
{
    private readonly IRoleRepository _roles;

    public GetAdminRoleByIdHandler(IRoleRepository roles)
    {
        _roles = roles;
    }

    public async Task<Result<AdminRoleResponse>> Handle(
        GetAdminRoleByIdQuery request,
        CancellationToken cancellationToken)
    {
        var role = await _roles.GetByIdAsync(request.Id, cancellationToken);
        if (role is null)
        {
            return Result.Failure<AdminRoleResponse>(AdminRoleErrors.NotFound);
        }

        return Result.Success(role.ToResponse());
    }
}
