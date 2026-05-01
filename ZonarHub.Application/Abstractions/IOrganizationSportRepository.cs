using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persistence contract for sport enablement per organization.
/// </summary>
public interface IOrganizationSportRepository
{
    Task<IReadOnlyList<SportId>> GetEnabledSportIdsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default);

    Task SetEnabledSportsAsync(
        OrganizationId organizationId,
        IEnumerable<SportId> sportIds,
        CancellationToken cancellationToken = default);
}
