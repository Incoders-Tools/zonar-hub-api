using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class RoleRepository : IRoleRepository
{
    private const string RestPath = "/rest/v1/roles";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public RoleRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<IReadOnlyList<RoleDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _http.GetFromJsonAsync<List<RoleRow>>(
            $"{RestPath}?select=*&order=name.asc",
            cancellationToken) ?? [];

        return rows.Select(ToModel).ToList();
    }

    public async Task<RoleDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var escapedId = Uri.EscapeDataString((id ?? string.Empty).Trim());
        var rows = await _http.GetFromJsonAsync<List<RoleRow>>(
            $"{RestPath}?select=*&id=eq.{escapedId}&limit=1",
            cancellationToken);

        var row = rows?.FirstOrDefault();
        return row is null ? null : ToModel(row);
    }

    public async Task<RoleDefinition?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = (name ?? string.Empty).Trim().ToLowerInvariant();
        var escapedName = Uri.EscapeDataString(normalizedName);
        var rows = await _http.GetFromJsonAsync<List<RoleRow>>(
            $"{RestPath}?select=*&name=eq.{escapedName}&limit=1",
            cancellationToken);

        var row = rows?.FirstOrDefault();
        return row is null ? null : ToModel(row);
    }

    public Task AddAsync(RoleDefinition role, CancellationToken cancellationToken = default)
    {
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, ToRow(role), ct));
        return Task.CompletedTask;
    }

    public void Update(RoleDefinition role)
    {
        _ops.Enqueue((http, ct) => ExecutePatchAsync(http, role.Id, ToPatchRow(role), ct));
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        var escapedId = Uri.EscapeDataString((id ?? string.Empty).Trim());
        _ops.Enqueue((http, ct) => ExecuteDeleteAsync(http, escapedId, ct));
        return Task.CompletedTask;
    }

    private static async Task ExecuteAddAsync(HttpClient http, RoleRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecutePatchAsync(HttpClient http, string id, RolePatchRow patch, CancellationToken ct)
    {
        var escapedId = Uri.EscapeDataString((id ?? string.Empty).Trim());
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{escapedId}");
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(patch);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecuteDeleteAsync(HttpClient http, string escapedId, CancellationToken ct)
    {
        using var resp = await http.DeleteAsync($"{RestPath}?id=eq.{escapedId}", ct);
        resp.EnsureSuccessStatusCode();
    }

    private static RoleDefinition ToModel(RoleRow row) => new(
        row.Id,
        row.Name,
        row.Description,
        row.IsActive,
        row.IsSystem,
        row.CreatedAtUtc,
        row.UpdatedAtUtc);

    private static RoleRow ToRow(RoleDefinition role) => new(
        role.Id,
        role.Name,
        role.Description,
        role.IsActive,
        role.IsSystem,
        role.CreatedAtUtc,
        role.UpdatedAtUtc);

    private static RolePatchRow ToPatchRow(RoleDefinition role) => new(
        role.Name,
        role.Description,
        role.IsActive,
        role.UpdatedAtUtc);

    private sealed record RoleRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("is_system")] bool IsSystem,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record RolePatchRow(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
