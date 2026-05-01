using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Sports;
using ZonarHub.Domain.Tenants;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class TenantSportRepository : ITenantSportRepository
{
    private const string RestPath = "/rest/v1/tenant_sports";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public TenantSportRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<IReadOnlyList<SportId>> GetEnabledSportIdsAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?select=sport_id&tenant_id=eq.{tenantId.Value}";
        var rows = await _http.GetFromJsonAsync<List<TenantSportRow>>(url, cancellationToken) ?? [];

        return rows
            .Select(r => new SportId(r.SportId))
            .Distinct()
            .ToList();
    }

    public Task SetEnabledSportsAsync(
        TenantId tenantId,
        IEnumerable<SportId> sportIds,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = sportIds.Distinct().ToList();

        _ops.Enqueue((http, ct) => DeleteForTenantAsync(http, tenantId.Value, ct));

        if (distinctIds.Count > 0)
        {
            var rows = distinctIds
                .Select(id => new TenantSportRow(tenantId.Value, id.Value))
                .ToList();

            _ops.Enqueue((http, ct) => InsertRowsAsync(http, rows, ct));
        }

        return Task.CompletedTask;
    }

    private static async Task DeleteForTenantAsync(HttpClient http, Guid tenantId, CancellationToken ct)
    {
        using var resp = await http.DeleteAsync($"{RestPath}?tenant_id=eq.{tenantId}", ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task InsertRowsAsync(
        HttpClient http,
        IReadOnlyList<TenantSportRow> rows,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(rows);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record TenantSportRow(
        [property: JsonPropertyName("tenant_id")] Guid TenantId,
        [property: JsonPropertyName("sport_id")] Guid SportId);
}
