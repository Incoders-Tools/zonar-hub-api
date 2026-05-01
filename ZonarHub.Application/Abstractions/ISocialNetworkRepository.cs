using ZonarHub.Domain.SocialNetworks;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persistence contract for the <see cref="SocialNetwork"/> aggregate.
/// </summary>
public interface ISocialNetworkRepository
{
    Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken = default);

    Task<SocialNetwork?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SocialNetwork> Items, int TotalCount)> ListAsync(
        SocialNetworkQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(SocialNetwork network, CancellationToken cancellationToken = default);

    void Update(SocialNetwork network);

    void Remove(SocialNetwork network);
}

/// <summary>
/// Repository-facing filter + pagination spec for listing social networks.
/// </summary>
public sealed record SocialNetworkQuery(
    string? NameContains,
    bool? IsActive,
    int Page,
    int PageSize);
