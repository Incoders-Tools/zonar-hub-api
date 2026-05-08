using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryTournamentRuleRepository : ITournamentRuleRepository
{
    private readonly Dictionary<Guid, TournamentRuleDto> _store = new();

    public Task<IReadOnlyList<TournamentRuleDto>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<TournamentRuleDto> query = _store.Values;
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        IReadOnlyList<TournamentRuleDto> ordered = query
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(ordered);
    }

    public Task<TournamentRuleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(id, out var dto);
        return Task.FromResult(dto);
    }

    public Task<TournamentRuleDto> AddAsync(TournamentRuleDto rule, CancellationToken cancellationToken = default)
    {
        _store[rule.Id] = rule;
        return Task.FromResult(rule);
    }

    public Task<TournamentRuleDto> UpdateAsync(TournamentRuleDto rule, CancellationToken cancellationToken = default)
    {
        _store[rule.Id] = rule;
        return Task.FromResult(rule);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }
}
