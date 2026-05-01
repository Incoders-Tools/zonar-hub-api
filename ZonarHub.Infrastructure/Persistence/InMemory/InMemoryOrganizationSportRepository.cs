using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryOrganizationSportRepository : IOrganizationSportRepository
{
    private readonly InMemoryOrganizationSportStore _store;

    public InMemoryOrganizationSportRepository(InMemoryOrganizationSportStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<SportId>> GetEnabledSportIdsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        if (_store.Data.TryGetValue(organizationId, out var ids))
        {
            IReadOnlyList<SportId> result = ids.ToList();
            return Task.FromResult(result);
        }

        return Task.FromResult<IReadOnlyList<SportId>>(Array.Empty<SportId>());
    }

    public Task SetEnabledSportsAsync(
        OrganizationId organizationId,
        IEnumerable<SportId> sportIds,
        CancellationToken cancellationToken = default)
    {
        _store.Data[organizationId] = new HashSet<SportId>(sportIds);
        return Task.CompletedTask;
    }
}
