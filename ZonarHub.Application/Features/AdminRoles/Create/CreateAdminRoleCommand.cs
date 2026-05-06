using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.Create;

public sealed record CreateAdminRoleCommand(
    string Name,
    string Description,
    bool IsActive) : IRequest<Result<AdminRoleResponse>>;
