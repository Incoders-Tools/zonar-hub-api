using ZonarHub.Domain.Organizations;

namespace ZonarHub.Application.Abstractions;

/// <summary>
/// Persistence contract for the <see cref="Organization"/> aggregate.
/// </summary>
public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(OrganizationId id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Organization> Items, int TotalCount)> ListAsync(
        OrganizationQuery query,
        CancellationToken cancellationToken = default);

    Task AddAsync(Organization organization, CancellationToken cancellationToken = default);

    void Update(Organization organization);

    void Remove(Organization organization);
}

/// <summary>
/// Repository-facing filter + pagination spec for listing organizations.
/// </summary>
public sealed record OrganizationQuery(
    Guid? TenantId,
    string? DisplayNameContains,
    string? Type,
    bool? IsActive,
    int Page,
    int PageSize);
