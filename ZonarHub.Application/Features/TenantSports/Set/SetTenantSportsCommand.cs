using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.TenantSports.Set;

public sealed record SetTenantSportsCommand(
    Guid TenantId,
    IEnumerable<Guid> EnabledSportIds,
    Guid? RequiredTenantId = null) : IRequest<Result>;
