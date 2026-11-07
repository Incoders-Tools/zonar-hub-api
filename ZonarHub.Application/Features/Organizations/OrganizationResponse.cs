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
    DateTime UpdatedAt,
    bool PublicProfileEnabled,
    string? WebsiteUrl,
    string? InstagramUrl,
    string? FacebookUrl,
    string? TiktokUrl,
    string? YoutubeUrl,
    string? WhatsappPhone,
    string? ContactEmail)
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
            org.UpdatedAtUtc,
            org.PublicProfile.Enabled,
            org.PublicProfile.WebsiteUrl,
            org.PublicProfile.InstagramUrl,
            org.PublicProfile.FacebookUrl,
            org.PublicProfile.TiktokUrl,
            org.PublicProfile.YoutubeUrl,
            org.PublicProfile.WhatsappPhone,
            org.PublicProfile.ContactEmail);
}
