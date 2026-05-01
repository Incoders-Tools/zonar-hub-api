using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Tenants;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryTenantRepository : ITenantRepository
{
    private readonly InMemoryTenantStore _store;

    public InMemoryTenantRepository(InMemoryTenantStore store)
    {
        _store = store;
    }

    public Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryGetValue(id, out var tenant);
        return Task.FromResult(tenant);
    }

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(tenant.Id, tenant))
            throw new InvalidOperationException($"Tenant '{tenant.Id}' already exists.");
        return Task.CompletedTask;
    }

    public void Update(Tenant tenant)
    {
        _store.Data[tenant.Id] = tenant;
    }
}
