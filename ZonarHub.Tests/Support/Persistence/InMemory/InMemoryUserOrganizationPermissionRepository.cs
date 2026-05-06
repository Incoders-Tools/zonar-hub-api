using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.InMemory;

public sealed class InMemoryUserOrganizationPermissionRepository : IUserOrganizationPermissionRepository
{
    private readonly InMemoryUserOrganizationPermissionStore _store;

    public InMemoryUserOrganizationPermissionRepository(InMemoryUserOrganizationPermissionStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<UserOrganizationPermission>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (_store.Data.TryGetValue(userId, out var permissions))
        {
            return Task.FromResult<IReadOnlyList<UserOrganizationPermission>>(permissions.ToList());
        }

        return Task.FromResult<IReadOnlyList<UserOrganizationPermission>>([]);
    }

    public Task<IReadOnlyList<string>> GetToolKeysAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        if (!_store.Data.TryGetValue(userId, out var permissions))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var tools = permissions
            .Where(permission => permission.OrganizationId == organizationId)
            .Select(permission => permission.ToolKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(tools);
    }

    public Task SetPermissionsAsync(
        Guid userId,
        IReadOnlyList<UserOrganizationPermission> permissions,
        CancellationToken cancellationToken = default)
    {
        var normalized = permissions
            .Where(permission => permission.OrganizationId != Guid.Empty && !string.IsNullOrWhiteSpace(permission.ToolKey))
            .Select(permission => new UserOrganizationPermission(
                permission.OrganizationId,
                permission.ToolKey.Trim().ToLowerInvariant()))
            .DistinctBy(permission => (permission.OrganizationId, permission.ToolKey))
            .ToList();

        _store.Data[userId] = normalized;
        return Task.CompletedTask;
    }

    public Task RemoveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _store.Data.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
