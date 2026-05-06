using MediatR;
using ZonarHub.Domain.Common;

namespace ZonarHub.Application.Features.AdminPermissions.GetEffective;

public sealed record GetEffectivePermissionsQuery(Guid? OrganizationId) : IRequest<Result<EffectivePermissionsResponse>>;
