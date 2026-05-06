using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class UserOrganizationPermissionRepository : IUserOrganizationPermissionRepository
{
    private const string RestPath = "/rest/v1/user_organization_permissions";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public UserOrganizationPermissionRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<IReadOnlyList<UserOrganizationPermission>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<UserOrganizationPermissionRow>>(
            $"{RestPath}?select=organization_id,tool_key&user_id=eq.{userId}&order=organization_id.asc",
            cancellationToken) ?? [];

        return rows
            .Select(row => new UserOrganizationPermission(row.OrganizationId, row.ToolKey))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetToolKeysAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<UserOrganizationToolRow>>(
            $"{RestPath}?select=tool_key&user_id=eq.{userId}&organization_id=eq.{organizationId}",
            cancellationToken) ?? [];

        return rows
            .Select(row => row.ToolKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        _ops.Enqueue((http, ct) => DeleteByUserAsync(http, userId, ct));

        if (normalized.Count > 0)
        {
            var rows = normalized
                .Select(permission => new UserOrganizationPermissionWriteRow(
                    userId,
                    permission.OrganizationId,
                    permission.ToolKey))
                .ToList();

            _ops.Enqueue((http, ct) => InsertRowsAsync(http, rows, ct));
        }

        return Task.CompletedTask;
    }

    public Task RemoveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _ops.Enqueue((http, ct) => DeleteByUserAsync(http, userId, ct));
        return Task.CompletedTask;
    }

    private static async Task DeleteByUserAsync(HttpClient http, Guid userId, CancellationToken cancellationToken)
    {
        using var response = await http.DeleteAsync($"{RestPath}?user_id=eq.{userId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task InsertRowsAsync(
        HttpClient http,
        IReadOnlyList<UserOrganizationPermissionWriteRow> rows,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RestPath);
        request.Headers.Add("Prefer", "return=minimal");
        request.Content = JsonContent.Create(rows);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record UserOrganizationPermissionRow(
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("tool_key")] string ToolKey);

    private sealed record UserOrganizationToolRow(
        [property: JsonPropertyName("tool_key")] string ToolKey);

    private sealed record UserOrganizationPermissionWriteRow(
        [property: JsonPropertyName("user_id")] Guid UserId,
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("tool_key")] string ToolKey);
}
