using ZonarHub.Domain.Tournaments;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Application.Abstractions;

public interface ITournamentRepository
{
    Task<Tournament?> GetByIdAsync(TournamentId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tournament>> ListByOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default);
}
