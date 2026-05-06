using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.Update;

public sealed record UpdateAdminRoleCommand(
    string Id,
    string? Name,
    string? Description,
    bool? IsActive) : IRequest<Result<AdminRoleResponse>>;
