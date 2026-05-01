using ZonarHub.Domain.Tournaments;

namespace ZonarHub.Application.Abstractions;

public interface ITournamentRepository
{
    Task<Tournament?> GetByIdAsync(TournamentId id, CancellationToken cancellationToken = default);

    Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default);
}
