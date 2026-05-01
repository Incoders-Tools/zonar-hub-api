using ZonarHub.Application.Features.Organizations;
using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.GetById;

public sealed record GetOrganizationByIdQuery(Guid Id, Guid? RequiredTenantId = null)
    : IRequest<Result<OrganizationResponse>>;
