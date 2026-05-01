using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryTournamentRepository : ITournamentRepository
{
    private readonly InMemoryTournamentStore _store;

    public InMemoryTournamentRepository(InMemoryTournamentStore store)
    {
        _store = store;
    }

    public Task<Tournament?> GetByIdAsync(TournamentId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryGetValue(id, out var tournament);
        return Task.FromResult(tournament);
    }

    public Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(tournament.Id, tournament))
            throw new InvalidOperationException($"Tournament '{tournament.Id}' already exists.");

        return Task.CompletedTask;
    }
}
