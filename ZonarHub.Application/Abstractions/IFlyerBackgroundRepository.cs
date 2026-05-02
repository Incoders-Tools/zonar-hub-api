using ZonarHub.Domain.FlyerBackgrounds;

namespace ZonarHub.Application.Abstractions;

public interface IFlyerBackgroundRepository
{
    Task<FlyerBackground?> GetByIdAsync(FlyerBackgroundId id, CancellationToken cancellationToken = default);
    Task<FlyerBackground?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<FlyerBackground> Items, int TotalCount)> ListAsync(
        FlyerBackgroundQuery query,
        CancellationToken cancellationToken = default);
    Task AddAsync(FlyerBackground background, CancellationToken cancellationToken = default);
    void Update(FlyerBackground background);
    void Remove(FlyerBackground background);
}

public sealed record FlyerBackgroundQuery(
    string? NameContains,
    string? Category,
    bool? IsActive,
    int Page,
    int PageSize);
