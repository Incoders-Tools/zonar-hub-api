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
    Guid? RequiredTenantId = null,
    bool PublicProfileEnabled = false,
    string? WebsiteUrl = null,
    string? InstagramUrl = null,
    string? FacebookUrl = null,
    string? TiktokUrl = null,
    string? YoutubeUrl = null,
    string? WhatsappPhone = null,
    string? ContactEmail = null) : IRequest<Result<OrganizationResponse>>, IOrganizationPublicProfileFields;
