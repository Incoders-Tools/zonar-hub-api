namespace ZonarHub.Application.Abstractions;

public interface ISystemPermissionCatalogRepository
{
    Task<IReadOnlyList<SystemModuleDefinition>> ListModulesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemToolDefinition>> ListToolsByKeysAsync(
        IReadOnlyCollection<string> toolKeys,
        CancellationToken cancellationToken = default);
}

public sealed record SystemModuleDefinition(
    string Id,
    string Key,
    string LabelKey,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<SystemToolDefinition> Tools);

public sealed record SystemToolDefinition(
    string Id,
    string Key,
    string ModuleKey,
    string LabelKey,
    string? Route,
    int SortOrder,
    bool IsSystemAdminOnly,
    bool IsActive);
