using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Users;

namespace ZonarHub.Application.Features.AdminPermissions;

public sealed class UserPermissionService : IUserPermissionService
{
    private static readonly HashSet<string> EditorModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "dashboard",
        "circuit",
        "catalog"
    };

    private readonly IUserOrganizationAssignmentRepository _assignments;
    private readonly IUserOrganizationPermissionRepository _permissions;
    private readonly ISystemPermissionCatalogRepository _catalog;

    public UserPermissionService(
        IUserOrganizationAssignmentRepository assignments,
        IUserOrganizationPermissionRepository permissions,
        ISystemPermissionCatalogRepository catalog)
    {
        _assignments = assignments;
        _permissions = permissions;
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<string>> GetEffectiveToolKeysAsync(
        User user,
        Guid? organizationId,
        CancellationToken cancellationToken = default)
    {
        var activeTools = await GetActiveToolsAsync(cancellationToken);

        if (user.Role == UserRole.SystemAdmin)
        {
            return activeTools
                .Select(tool => tool.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var scopeOrganizationId = organizationId ?? user.OrganizationId;
        if (!scopeOrganizationId.HasValue)
        {
            var defaultsWithoutScope = await GetDefaultToolKeysAsync(user.Role, cancellationToken);
            return FilterActiveTools(defaultsWithoutScope, activeTools, allowRestricted: false);
        }

        var assignedOrganizationIds = await _assignments.GetOrganizationIdsByUserIdAsync(user.Id.Value, cancellationToken);
        var allowedOrganizations = new HashSet<Guid>(assignedOrganizationIds);
        if (user.OrganizationId.HasValue)
        {
            allowedOrganizations.Add(user.OrganizationId.Value);
        }

        if (!allowedOrganizations.Contains(scopeOrganizationId.Value))
        {
            return [];
        }

        var persistedToolKeys = await _permissions.GetToolKeysAsync(
            user.Id.Value,
            scopeOrganizationId.Value,
            cancellationToken);

        if (persistedToolKeys.Count > 0)
        {
            return FilterActiveTools(persistedToolKeys, activeTools, allowRestricted: false);
        }

        var defaults = await GetDefaultToolKeysAsync(user.Role, cancellationToken);
        return FilterActiveTools(defaults, activeTools, allowRestricted: false);
    }

    public async Task<bool> HasToolAsync(
        User user,
        Guid? organizationId,
        string toolKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolKey))
        {
            return false;
        }

        var tools = await GetEffectiveToolKeysAsync(user, organizationId, cancellationToken);
        return tools.Contains(toolKey, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> GetDefaultToolKeysAsync(
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var activeTools = await GetActiveToolsAsync(cancellationToken);

        if (role == UserRole.SystemAdmin)
        {
            return activeTools
                .Select(tool => tool.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (role == UserRole.Admin)
        {
            return activeTools
                .Where(tool => !tool.IsSystemAdminOnly)
                .Select(tool => tool.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (role == UserRole.Editor)
        {
            return activeTools
                .Where(tool => !tool.IsSystemAdminOnly && EditorModules.Contains(tool.ModuleKey))
                .Select(tool => tool.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return activeTools
            .Where(tool => tool.Key.Equals("dashboard", StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<SystemToolDefinition>> GetActiveToolsAsync(CancellationToken cancellationToken)
    {
        var tools = await _catalog.ListToolsAsync(cancellationToken);
        return tools.Where(tool => tool.IsActive).ToList();
    }

    private static IReadOnlyList<string> FilterActiveTools(
        IEnumerable<string> toolKeys,
        IReadOnlyList<SystemToolDefinition> activeTools,
        bool allowRestricted)
    {
        var catalog = activeTools
            .Where(tool => allowRestricted || !tool.IsSystemAdminOnly)
            .ToDictionary(tool => tool.Key, StringComparer.OrdinalIgnoreCase);

        return toolKeys
            .Where(toolKey => catalog.ContainsKey(toolKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
