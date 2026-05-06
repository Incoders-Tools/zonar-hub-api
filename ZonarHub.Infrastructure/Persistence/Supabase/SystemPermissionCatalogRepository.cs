using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class SystemPermissionCatalogRepository : ISystemPermissionCatalogRepository
{
    private const string ModulesRestPath = "/rest/v1/system_modules";
    private const string ToolsRestPath = "/rest/v1/system_tools";

    private readonly HttpClient _http;

    public SystemPermissionCatalogRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<IReadOnlyList<SystemModuleDefinition>> ListModulesAsync(CancellationToken cancellationToken = default)
    {
        var moduleRows = await ReadModuleRowsAsync(cancellationToken);
        var toolRows = await ReadToolRowsAsync(cancellationToken);

        var toolsByModuleId = toolRows
            .GroupBy(tool => tool.ModuleId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SystemToolDefinition>)group
                    .OrderBy(tool => tool.SortOrder)
                    .ThenBy(tool => tool.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(tool => new SystemToolDefinition(
                        tool.Id,
                        tool.Key,
                        string.Empty,
                        tool.LabelKey,
                        tool.Route,
                        tool.SortOrder,
                        tool.IsSystemAdminOnly,
                        tool.IsActive))
                    .ToList());

        return moduleRows
            .OrderBy(module => module.SortOrder)
            .ThenBy(module => module.Key, StringComparer.OrdinalIgnoreCase)
            .Select(module =>
            {
                var tools = toolsByModuleId.TryGetValue(module.Id, out var list)
                    ? list.Select(tool => tool with { ModuleKey = module.Key }).ToList()
                    : [];

                return new SystemModuleDefinition(
                    module.Id,
                    module.Key,
                    module.LabelKey,
                    module.SortOrder,
                    module.IsActive,
                    tools);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SystemToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var moduleRows = await ReadModuleRowsAsync(cancellationToken);
        var moduleKeyById = moduleRows.ToDictionary(module => module.Id, module => module.Key, StringComparer.OrdinalIgnoreCase);
        var toolRows = await ReadToolRowsAsync(cancellationToken);

        return toolRows
            .OrderBy(tool => tool.SortOrder)
            .ThenBy(tool => tool.Key, StringComparer.OrdinalIgnoreCase)
            .Select(tool => new SystemToolDefinition(
                tool.Id,
                tool.Key,
                moduleKeyById.TryGetValue(tool.ModuleId, out var moduleKey) ? moduleKey : string.Empty,
                tool.LabelKey,
                tool.Route,
                tool.SortOrder,
                tool.IsSystemAdminOnly,
                tool.IsActive))
            .ToList();
    }

    public async Task<IReadOnlyList<SystemToolDefinition>> ListToolsByKeysAsync(
        IReadOnlyCollection<string> toolKeys,
        CancellationToken cancellationToken = default)
    {
        if (toolKeys.Count == 0)
        {
            return [];
        }

        var requested = new HashSet<string>(toolKeys, StringComparer.OrdinalIgnoreCase);
        var allTools = await ListToolsAsync(cancellationToken);

        return allTools
            .Where(tool => requested.Contains(tool.Key))
            .ToList();
    }

    private async Task<IReadOnlyList<SystemModuleRow>> ReadModuleRowsAsync(CancellationToken cancellationToken)
    {
        return await _http.GetFromJsonAsync<List<SystemModuleRow>>(
            $"{ModulesRestPath}?select=id,key,label_key,sort_order,is_active&order=sort_order.asc",
            cancellationToken) ?? [];
    }

    private async Task<IReadOnlyList<SystemToolRow>> ReadToolRowsAsync(CancellationToken cancellationToken)
    {
        return await _http.GetFromJsonAsync<List<SystemToolRow>>(
            $"{ToolsRestPath}?select=id,key,module_id,label_key,route,sort_order,is_system_admin_only,is_active&order=sort_order.asc",
            cancellationToken) ?? [];
    }

    private sealed record SystemModuleRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("label_key")] string LabelKey,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_active")] bool IsActive);

    private sealed record SystemToolRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("module_id")] string ModuleId,
        [property: JsonPropertyName("label_key")] string LabelKey,
        [property: JsonPropertyName("route")] string? Route,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_system_admin_only")] bool IsSystemAdminOnly,
        [property: JsonPropertyName("is_active")] bool IsActive);
}
