using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Complexes;
using ZonarHub.Domain.Courts;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryCourtRepository : ICourtRepository
{
    private readonly InMemoryCourtStore _store;

    public InMemoryCourtRepository(InMemoryCourtStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<Court>> ListByComplexIdAsync(
        ComplexId complexId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Court> result = _store.Data.Values
            .Where(c => c.ComplexId == complexId)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(result);
    }

    public Task AddAsync(Court court, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(court.Id, court))
            throw new InvalidOperationException($"Court '{court.Id}' already exists.");

        return Task.CompletedTask;
    }
}
