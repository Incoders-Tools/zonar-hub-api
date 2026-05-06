using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminPermissions.GetUserPermissions;

public sealed record GetAdminUserPermissionsQuery(Guid UserId) : IRequest<Result<AdminUserPermissionsResponse>>;
