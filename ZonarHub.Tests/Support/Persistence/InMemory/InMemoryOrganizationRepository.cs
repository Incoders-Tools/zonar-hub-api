using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryOrganizationRepository : IOrganizationRepository
{
    private readonly InMemoryOrganizationStore _store;

    public InMemoryOrganizationRepository(InMemoryOrganizationStore store)
    {
        _store = store;
    }

    public Task<Organization?> GetByIdAsync(OrganizationId id, CancellationToken cancellationToken = default)
    {
        _store.Data.TryGetValue(id, out var org);
        return Task.FromResult(org);
    }

    public Task<Organization?> GetByTenantAndDisplayNameAsync(
        Guid tenantId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var normalized = displayName.Trim();
        var match = _store.Data.Values.FirstOrDefault(o =>
            o.TenantId == tenantId &&
            string.Equals(o.DisplayName, normalized, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(match);
    }

    public Task<(IReadOnlyList<Organization> Items, int TotalCount)> ListAsync(
        OrganizationQuery query,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Organization> source = _store.Data.Values;

        if (query.TenantId is { } tenantId)
        {
            source = source.Where(o => o.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(query.DisplayNameContains))
        {
            var needle = query.DisplayNameContains.Trim();
            source = source.Where(o =>
                o.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (o.LegalName != null && o.LegalName.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            source = source.Where(o =>
                string.Equals(o.Type.ToString(), query.Type, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(o => o.IsActive == isActive);
        }

        var filtered = source
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = filtered.Count;
        IReadOnlyList<Organization> page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult((page, total));
    }

    public Task AddAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryAdd(organization.Id, organization))
        {
            throw new InvalidOperationException($"Organization '{organization.Id}' already exists.");
        }

        return Task.CompletedTask;
    }

    public void Update(Organization organization)
    {
        _store.Data[organization.Id] = organization;
    }

    public void Remove(Organization organization)
    {
        _store.Data.TryRemove(organization.Id, out _);
    }
}
