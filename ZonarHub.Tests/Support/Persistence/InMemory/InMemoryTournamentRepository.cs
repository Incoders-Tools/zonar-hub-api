using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Tournaments;
using ZonarHub.Domain.Organizations;

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

    public Task<IReadOnlyList<Tournament>> ListByOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Tournament> result = _store.Data.Values
            .Where(t => t.OrganizationId == organizationId)
            .OrderByDescending(t => t.StartDate)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(result);
    }

    public Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(tournament.Id, tournament))
            throw new InvalidOperationException($"Tournament '{tournament.Id}' already exists.");

        return Task.CompletedTask;
    }
}
