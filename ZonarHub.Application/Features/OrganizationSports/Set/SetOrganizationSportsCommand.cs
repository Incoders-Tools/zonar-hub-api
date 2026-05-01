using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.OrganizationSports.Set;

public sealed record SetOrganizationSportsCommand(
    Guid OrganizationId,
    IEnumerable<Guid> EnabledSportIds,
    Guid? RequiredTenantId = null) : IRequest<Result>;
