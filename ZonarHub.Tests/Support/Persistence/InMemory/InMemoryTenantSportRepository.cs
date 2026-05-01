using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tenants;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryTenantSportRepository : ITenantSportRepository
{
    private readonly InMemoryTenantSportStore _store;

    public InMemoryTenantSportRepository(InMemoryTenantSportStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<SportId>> GetEnabledSportIdsAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        if (_store.Data.TryGetValue(tenantId, out var ids))
        {
            IReadOnlyList<SportId> result = ids.ToList();
            return Task.FromResult(result);
        }

        return Task.FromResult<IReadOnlyList<SportId>>(Array.Empty<SportId>());
    }

    public Task SetEnabledSportsAsync(
        TenantId tenantId,
        IEnumerable<SportId> sportIds,
        CancellationToken cancellationToken = default)
    {
        _store.Data[tenantId] = new HashSet<SportId>(sportIds);
        return Task.CompletedTask;
    }
}
