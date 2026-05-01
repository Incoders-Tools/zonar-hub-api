using ZonarHub.Domain.SocialNetworks;

namespace ZonarHub.Application.Features.SocialNetworks;

public sealed record SocialNetworkResponse(
    Guid Id,
    string Name,
    string Key,
    string? Url,
    string? Description,
    string? FaIcon,
    int? SortOrder,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static SocialNetworkResponse FromDomain(SocialNetwork network) =>
        new(
            network.Id.Value,
            network.Name,
            network.Key,
            network.Url,
            network.Description,
            network.FaIcon,
            network.SortOrder,
            network.IsActive,
            network.CreatedAtUtc,
            network.UpdatedAtUtc);
}
