using ZonarHub.Application.Features.Organizations;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.Update;

public sealed record UpdateOrganizationCommand(
    Guid Id,
    string DisplayName,
    string? LegalName,
    string? Description,
    OrganizationType Type,
    string? LogoUrl,
    bool IsActive,
    Guid? RequiredTenantId = null) : IRequest<Result<OrganizationResponse>>;
