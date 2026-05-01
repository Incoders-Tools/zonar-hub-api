using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.SocialNetworks;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemorySocialNetworkRepository : ISocialNetworkRepository
{
    private readonly InMemorySocialNetworkStore _store;

    public InMemorySocialNetworkRepository(InMemorySocialNetworkStore store)
    {
        _store = store;
    }

    public Task<SocialNetwork?> GetByIdAsync(SocialNetworkId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryGetValue(id, out var network);
        return Task.FromResult(network);
    }

    public Task<SocialNetwork?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var match = _store.Data.Values.FirstOrDefault(n =>
            string.Equals(n.Key, key, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<(IReadOnlyList<SocialNetwork> Items, int TotalCount)> ListAsync(
        SocialNetworkQuery query,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<SocialNetwork> source = _store.Data.Values;

        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            var needle = query.NameContains.Trim();
            source = source.Where(n => n.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(n => n.IsActive == isActive);
        }

        var filtered = source
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = filtered.Count;
        IReadOnlyList<SocialNetwork> page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult((page, total));
    }

    public Task AddAsync(SocialNetwork network, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(network.Id, network))
        {
            throw new InvalidOperationException($"SocialNetwork '{network.Id}' already exists.");
        }

        return Task.CompletedTask;
    }

    public void Update(SocialNetwork network)
    {
        _store.Data[network.Id] = network;
    }

    public void Remove(SocialNetwork network)
    {
        _store.Data.TryRemove(network.Id, out _);
    }
}
