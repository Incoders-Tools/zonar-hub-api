using ZonarHub.Domain.Organizations;

namespace ZonarHub.Application.Features.Organizations;

public sealed record OrganizationResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string? LegalName,
    string? Description,
    string Type,
    string? LogoUrl,
    bool IsActive,
    Guid CreatedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static OrganizationResponse FromDomain(Organization org) =>
        new(
            org.Id.Value,
            org.TenantId,
            org.DisplayName,
            org.LegalName,
            org.Description,
            org.Type.ToString().ToLowerInvariant(),
            org.LogoUrl,
            org.IsActive,
            org.CreatedByUserId,
            org.CreatedAtUtc,
            org.UpdatedAtUtc);
}
