using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.OrganizationSports.Get;

public sealed record GetOrganizationSportsQuery(Guid OrganizationId, Guid? RequiredTenantId = null)
    : IRequest<Result<OrganizationSportsResponse>>;
