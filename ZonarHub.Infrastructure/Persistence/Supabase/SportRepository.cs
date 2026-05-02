using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class SportRepository : ISportRepository
{
    private const string RestPath = "/rest/v1/sports";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public SportRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<Sport?> GetByIdAsync(SportId id, CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?select=*&id=eq.{id.Value}";
        var rows = await _http.GetFromJsonAsync<List<SportRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<Sport?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var escaped = Uri.EscapeDataString(key.Trim().ToLowerInvariant());
        var url = $"{RestPath}?select=*&key=eq.{escaped}";
        var rows = await _http.GetFromJsonAsync<List<SportRow>>(url, cancellationToken);
        var row = rows?.FirstOrDefault();
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<Sport>> GetByIdsAsync(
        IEnumerable<SportId> ids,
        CancellationToken cancellationToken = default)
    {
        var values = ids.Select(id => id.Value).Distinct().ToList();
        if (values.Count == 0)
        {
            return Array.Empty<Sport>();
        }

        var inClause = string.Join(",", values.Select(v => v.ToString()));
        var url = $"{RestPath}?select=*&id=in.({inClause})";
        var rows = await _http.GetFromJsonAsync<List<SportRow>>(url, cancellationToken) ?? [];
        return rows.Select(ToDomain).ToList();
    }

    public async Task<(IReadOnlyList<Sport> Items, int TotalCount)> ListAsync(
        SportQuery query,
        CancellationToken cancellationToken = default)
    {
        var qs = BuildListQueryString(query);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?{qs}");
        req.Headers.Add("Prefer", "count=exact");

        using var resp = await _http.SendAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var totalCount = 0;
        if (resp.Content.Headers.TryGetValues("Content-Range", out var crValues))
        {
            var cr = crValues.FirstOrDefault();
            if (cr is not null)
            {
                var slash = cr.IndexOf('/');
                if (slash >= 0 && int.TryParse(cr[(slash + 1)..], out var total))
                {
                    totalCount = total;
                }
            }
        }

        var rows = await resp.Content.ReadFromJsonAsync<List<SportRow>>(cancellationToken) ?? [];
        return (rows.Select(ToDomain).ToList(), totalCount);
    }

    public Task AddAsync(Sport sport, CancellationToken cancellationToken = default)
    {
        var row = ToRow(sport);
        _ops.Enqueue((http, ct) => ExecuteAddAsync(http, row, ct));
        return Task.CompletedTask;
    }

    public void Update(Sport sport)
    {
        var patch = ToPatchRow(sport);
        _ops.Enqueue((http, ct) => ExecutePatchAsync(http, sport.Id.Value, patch, ct));
    }

    public void Remove(Sport sport)
    {
        var id = sport.Id.Value;
        _ops.Enqueue((http, ct) => ExecuteDeleteAsync(http, id, ct));
    }

    private static async Task ExecuteAddAsync(HttpClient http, SportRow row, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(row);
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task ExecutePatchAsync(
        HttpClient http,
        Guid id,
        SportPatchRow patch,
        CancellationToken ct)
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

    private static string BuildListQueryString(SportQuery q)
    {
        var parts = new List<string>
        {
            "select=*",
            "order=sort_order.asc,name.asc"
        };

        if (!string.IsNullOrWhiteSpace(q.NameContains))
        {
            parts.Add($"name=ilike.*{Uri.EscapeDataString(q.NameContains.Trim())}*");
        }

        if (q.IsActive.HasValue)
        {
            parts.Add($"is_active=eq.{q.IsActive.Value.ToString().ToLowerInvariant()}");
        }

        if (q.PageSize != int.MaxValue)
        {
            var offset = Math.Max(0, (q.Page - 1)) * q.PageSize;
            parts.Add($"offset={offset}");
            parts.Add($"limit={q.PageSize}");
        }

        return string.Join("&", parts);
    }

    private static Sport ToDomain(SportRow r) =>
        Sport.Reconstitute(
            new SportId(r.Id),
            r.Name,
            r.Key,
            r.Icon,
            Enum.Parse<SportIconSource>(r.IconSource, ignoreCase: true),
            r.ModalityIds,
            r.SortOrder,
            r.IsActive,
            r.CreatedAtUtc,
            r.UpdatedAtUtc);

    private static SportRow ToRow(Sport s) => new(
        s.Id.Value,
        s.Name,
        s.Key,
        s.Icon,
        s.IconSource.ToString().ToLowerInvariant(),
        s.ModalityIds.ToList(),
        s.SortOrder,
        s.IsActive,
        s.CreatedAtUtc,
        s.UpdatedAtUtc);

    private static SportPatchRow ToPatchRow(Sport s) => new(
        s.Name,
        s.Icon,
        s.IconSource.ToString().ToLowerInvariant(),
        s.ModalityIds.ToList(),
        s.SortOrder,
        s.IsActive,
        s.UpdatedAtUtc);

    private sealed record SportRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("icon")] string Icon,
        [property: JsonPropertyName("icon_source")] string IconSource,
        [property: JsonPropertyName("modality_ids")] List<Guid>? ModalityIds,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);

    private sealed record SportPatchRow(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("icon")] string Icon,
        [property: JsonPropertyName("icon_source")] string IconSource,
        [property: JsonPropertyName("modality_ids")] List<Guid> ModalityIds,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
