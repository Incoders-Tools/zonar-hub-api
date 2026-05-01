using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.TenantSports.Get;

public sealed record GetTenantSportsQuery(Guid TenantId, Guid? RequiredTenantId = null)
    : IRequest<Result<TenantSportsResponse>>;
