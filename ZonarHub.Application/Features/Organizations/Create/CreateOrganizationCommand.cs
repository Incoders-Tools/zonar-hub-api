using ZonarHub.Application.Features.Organizations;
using ZonarHub.Domain.Common;
using ZonarHub.Domain.Organizations;
using MediatR;

namespace ZonarHub.Application.Features.Organizations.Create;

public sealed record CreateOrganizationCommand(
    Guid TenantId,
    string DisplayName,
    string? LegalName,
    string? Description,
    OrganizationType Type,
    string? LogoUrl,
    Guid CreatedByUserId) : IRequest<Result<OrganizationResponse>>;
