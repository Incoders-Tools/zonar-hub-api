using ZonarHub.Domain.Tenants;

namespace ZonarHub.Application.Abstractions;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default);

    Task<Tenant?> GetByContactEmailAsync(string contactEmail, CancellationToken cancellationToken = default);

    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);

    void Update(Tenant tenant);
}
