using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class OrganizationRepository : IOrganizationRepository
{
    private const string RestPath = "/rest/v1/organizations";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public OrganizationRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<Organization?> GetByIdAsync(
        OrganizationId id,
        CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?select=*&id=eq.{id.Value}";
        var rows = await _http.GetFromJsonAsync<List<OrganizationRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<(IReadOnlyList<Organization> Items, int TotalCount)> ListAsync(
        OrganizationQuery query,
        CancellationToken cancellationToken = default)
    {
        var qs = BuildListQueryString(query);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?{qs}");
        req.Headers.Add("Prefer", "count=exact");

        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var totalCount = 0;
        if (resp.Headers.TryGetValues("Content-Range", out var crValues))
        {
            var cr = crValues.FirstOrDefault();
            if (cr is not null)
            {
                var slash = cr.IndexOf('/');
                if (slash >= 0 && int.TryParse(cr[(slash + 1)..], out var total))
                    totalCount = total;
            }
        }

        var rows = await resp.Content.ReadFromJsonAsync<List<OrganizationRow>>(cancellationToken) ?? [];
        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public Task AddAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        var row = ToRow(organization);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    public void Update(Organization organization)
    {
        var patch = ToPatchRow(organization);
        _ops.Enqueue((http, ct) => ExecutePatchAsync(http, organization.Id.Value, patch, ct));
    }

    public void Remove(Organization organization)
    {
        var id = organization.Id.Value;
        _ops.Enqueue((http, ct) => ExecuteDeleteAsync(http, id, ct));
    }

    // ---- Execute helpers ----

    private static async Task ExecuteAddAsync(HttpClient http, OrganizationRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecutePatchAsync(
        HttpClient http, Guid id, OrganizationPatchRow patch, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{id}");
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(patch);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecuteDeleteAsync(HttpClient http, Guid id, CancellationToken ct)
    {
        using var resp = await http.DeleteAsync($"{RestPath}?id=eq.{id}", ct);
        resp.EnsureSuccessStatusCode();
    }

    // ---- Query builder ----

    private static string BuildListQueryString(OrganizationQuery q)
    {
        var parts = new List<string> { "select=*" };

        if (q.TenantId.HasValue)
            parts.Add($"tenant_id=eq.{q.TenantId.Value}");

        if (!string.IsNullOrWhiteSpace(q.DisplayNameContains))
            parts.Add($"display_name=ilike.*{Uri.EscapeDataString(q.DisplayNameContains)}*");

        if (!string.IsNullOrWhiteSpace(q.Type))
            parts.Add($"type=eq.{Uri.EscapeDataString(q.Type.ToLowerInvariant())}");

        if (q.IsActive.HasValue)
            parts.Add($"is_active=eq.{q.IsActive.Value.ToString().ToLowerInvariant()}");

        var offset = Math.Max(0, (q.Page - 1)) * q.PageSize;
        parts.Add($"offset={offset}&limit={q.PageSize}");

        return string.Join("&", parts);
    }

    // ---- Mapping ----

    private static Organization ToDomain(OrganizationRow r) =>
        Organization.Reconstitute(
            new OrganizationId(Guid.Parse(r.Id)),
            Guid.Parse(r.TenantId),
            r.DisplayName,
            r.LegalName,
            r.Description,
            Enum.Parse<OrganizationType>(r.Type, ignoreCase: true),
            r.LogoUrl,
            r.IsActive,
            Guid.Parse(r.CreatedByUserId),
            r.CreatedAtUtc,
            r.UpdatedAtUtc);

    private static OrganizationRow ToRow(Organization o) => new(
        o.Id.Value.ToString(),
        o.TenantId.ToString(),
        o.DisplayName,
        o.LegalName,
        o.Description,
        o.Type.ToString().ToLowerInvariant(),
        o.LogoUrl,
        o.IsActive,
        o.CreatedByUserId.ToString(),
        o.CreatedAtUtc,
        o.UpdatedAtUtc);

    private static OrganizationPatchRow ToPatchRow(Organization o) => new(
        o.DisplayName,
        o.LegalName,
        o.Description,
        o.Type.ToString().ToLowerInvariant(),
        o.LogoUrl,
        o.IsActive,
        o.UpdatedAtUtc);

    // ---- DTOs ----

    private sealed record OrganizationRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("tenant_id")] string TenantId,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("legal_name")] string? LegalName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("logo_url")] string? LogoUrl,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_by_user_id")] string CreatedByUserId,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record OrganizationPatchRow(
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("legal_name")] string? LegalName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("logo_url")] string? LogoUrl,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
