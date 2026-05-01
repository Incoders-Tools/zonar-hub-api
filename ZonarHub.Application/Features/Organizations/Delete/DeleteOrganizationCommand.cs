using ZonarHub.Domain.Common;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.Delete;

public sealed record DeleteOrganizationCommand(Guid Id, Guid? RequiredTenantId = null)
    : IRequest<Result>;
