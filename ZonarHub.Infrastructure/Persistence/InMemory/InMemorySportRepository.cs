using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemorySportRepository : ISportRepository
{
    private readonly InMemorySportStore _store;

    public InMemorySportRepository(InMemorySportStore store)
    {
        _store = store;
    }

    public Task<Sport?> GetByIdAsync(SportId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryGetValue(id, out var sport);
        return Task.FromResult(sport);
    }

    public Task<Sport?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var match = _store.Data.Values.FirstOrDefault(s =>
            string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<IReadOnlyList<Sport>> GetByIdsAsync(
        IEnumerable<SportId> ids,
        CancellationToken cancellationToken = default)
    {
        var idSet = new HashSet<SportId>(ids);
        IReadOnlyList<Sport> result = _store.Data.Values
            .Where(s => idSet.Contains(s.Id))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<(IReadOnlyList<Sport> Items, int TotalCount)> ListAsync(
        SportQuery query,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Sport> source = _store.Data.Values;

        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            var needle = query.NameContains.Trim();
            source = source.Where(s => s.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(s => s.IsActive == isActive);
        }

        var filtered = source
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = filtered.Count;

        // Support int.MaxValue as "get all" (used by OrganizationSports handler)
        if (query.PageSize == int.MaxValue)
        {
            return Task.FromResult(((IReadOnlyList<Sport>)filtered, total));
        }

        IReadOnlyList<Sport> page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult((page, total));
    }

    public Task AddAsync(Sport sport, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(sport.Id, sport))
        {
            throw new InvalidOperationException($"Sport '{sport.Id}' already exists.");
        }

        return Task.CompletedTask;
    }

    public void Update(Sport sport)
    {
        _store.Data[sport.Id] = sport;
    }

    public void Remove(Sport sport)
    {
        _store.Data.TryRemove(sport.Id, out _);
    }
}
