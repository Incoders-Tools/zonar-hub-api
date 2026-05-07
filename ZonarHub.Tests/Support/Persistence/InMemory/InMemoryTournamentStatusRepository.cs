using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryTournamentStatusRepository : ITournamentStatusRepository
{
    private readonly Dictionary<Guid, TournamentStatusDto> _store = new();

    public Task<IReadOnlyList<TournamentStatusDto>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<TournamentStatusDto> query = _store.Values;
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        IReadOnlyList<TournamentStatusDto> ordered = query
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.NameEs, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(ordered);
    }

    public Task<TournamentStatusDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var dto);
        return Task.FromResult(dto);
    }

    public Task<TournamentStatusDto?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalized = key.Trim();
        var match = _store.Values.FirstOrDefault(s =>
            string.Equals(s.Key, normalized, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match);
    }

    public Task<TournamentStatusDto> AddAsync(TournamentStatusDto status, CancellationToken cancellationToken = default)
    {
        _store[status.Id] = status;
        return Task.FromResult(status);
    }

    public Task<TournamentStatusDto> UpdateAsync(TournamentStatusDto status, CancellationToken cancellationToken = default)
    {
        _store[status.Id] = status;
        return Task.FromResult(status);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }
}
