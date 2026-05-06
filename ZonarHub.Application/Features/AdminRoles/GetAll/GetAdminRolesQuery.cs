using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.GetAll;

public sealed record GetAdminRolesQuery : IRequest<Result<IReadOnlyList<AdminRoleResponse>>>;
