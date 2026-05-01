using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tenants;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persistence contract for sport enablement per tenant.
/// </summary>
public interface ITenantSportRepository
{
    Task<IReadOnlyList<SportId>> GetEnabledSportIdsAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    Task SetEnabledSportsAsync(
        TenantId tenantId,
        IEnumerable<SportId> sportIds,
        CancellationToken cancellationToken = default);
}
