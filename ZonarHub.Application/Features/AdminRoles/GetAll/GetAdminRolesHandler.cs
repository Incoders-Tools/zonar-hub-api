using MediatR;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.GetAll;

public sealed class GetAdminRolesHandler : IRequestHandler<GetAdminRolesQuery, Result<IReadOnlyList<AdminRoleResponse>>>
{
    private readonly IRoleRepository _roles;

    public GetAdminRolesHandler(IRoleRepository roles)
    {
        _roles = roles;
    }

    public async Task<Result<IReadOnlyList<AdminRoleResponse>>> Handle(
        GetAdminRolesQuery request,
        CancellationToken cancellationToken)
    {
        var roles = await _roles.ListAsync(cancellationToken);
        IReadOnlyList<AdminRoleResponse> mapped = roles.Select(role => role.ToResponse()).ToList();
        return Result.Success(mapped);
    }
}
