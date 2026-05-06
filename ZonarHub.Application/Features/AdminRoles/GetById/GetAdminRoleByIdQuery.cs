using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminRoles.GetById;

public sealed record GetAdminRoleByIdQuery(string Id) : IRequest<Result<AdminRoleResponse>>;
