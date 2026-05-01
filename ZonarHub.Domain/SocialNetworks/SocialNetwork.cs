using ZonarHub.Domain.Common;

namespace ZonarHub.Domain.SocialNetworks;

/// <summary>
/// Global social network catalog entry.
/// </summary>
public sealed class SocialNetwork : Entity<SocialNetworkId>
{
    private SocialNetwork(SocialNetworkId id) : base(id) { }

    public string Name { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string? Url { get; private set; }
    public string? Description { get; private set; }
    public string? FaIcon { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<SocialNetwork> Create(
        SocialNetworkId id,
        string name,
        string key,
        string? url,
        string? description,
        string? faIcon,
        int sortOrder,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<SocialNetwork>(SocialNetworkErrors.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<SocialNetwork>(SocialNetworkErrors.KeyRequired);
        }

        return Result.Success(new SocialNetwork(id)
        {
            Name = name.Trim(),
            Key = key.Trim().ToLowerInvariant(),
            Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            FaIcon = string.IsNullOrWhiteSpace(faIcon) ? null : faIcon.Trim(),
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        });
    }

    public Result Update(
        string name,
        string? url,
        string? description,
        string? faIcon,
        int sortOrder,
        bool isActive,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(SocialNetworkErrors.NameRequired);
        }

        Name = name.Trim();
        Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        FaIcon = string.IsNullOrWhiteSpace(faIcon) ? null : faIcon.Trim();
        SortOrder = sortOrder;
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }
}
