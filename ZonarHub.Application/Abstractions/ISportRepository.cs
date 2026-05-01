using ZonarHub.Domain.Sports;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persistence contract for the global <see cref="Sport"/> catalog.
/// </summary>
public interface ISportRepository
{
    Task<Sport?> GetByIdAsync(SportId id, CancellationToken cancellationToken = default);

    Task<Sport?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Sport>> GetByIdsAsync(IEnumerable<SportId> ids, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Sport> Items, int TotalCount)> ListAsync(
        SportQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(Sport sport, CancellationToken cancellationToken = default);

    void Update(Sport sport);

    void Remove(Sport sport);
}

/// <summary>
/// Repository-facing filter + pagination spec for listing sports.
/// </summary>
public sealed record SportQuery(
    string? NameContains,
    bool? IsActive,
    int Page,
    int PageSize);
