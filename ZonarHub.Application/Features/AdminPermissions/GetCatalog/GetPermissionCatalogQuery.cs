using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminPermissions.GetCatalog;

public sealed record GetPermissionCatalogQuery : IRequest<Result<PermissionCatalogResponse>>;
