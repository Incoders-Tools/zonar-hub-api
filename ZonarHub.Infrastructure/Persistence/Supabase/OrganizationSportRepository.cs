using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;
using ZonarHub.Domain.Organizations;
using ZonarHub.Domain.Sports;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class OrganizationSportRepository : IOrganizationSportRepository
{
    private const string RestPath = "/rest/v1/organization_sports";

    private readonly HttpClient _http;
    private readonly SupabaseOperationContext _ops;

    public OrganizationSportRepository(IHttpClientFactory factory, SupabaseOperationContext ops)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
        _ops = ops;
    }

    public async Task<IReadOnlyList<SportId>> GetEnabledSportIdsAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken = default)
    {
        var url = $"{RestPath}?select=sport_id&organization_id=eq.{organizationId.Value}";
        var rows = await _http.GetFromJsonAsync<List<OrganizationSportRow>>(url, cancellationToken) ?? [];

        return rows
            .Select(r => new SportId(r.SportId))
            .Distinct()
            .ToList();
    }

    public Task SetEnabledSportsAsync(
        OrganizationId organizationId,
        IEnumerable<SportId> sportIds,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = sportIds.Distinct().ToList();

        _ops.Enqueue((http, ct) => DeleteForOrganizationAsync(http, organizationId.Value, ct));

        if (distinctIds.Count > 0)
        {
            var rows = distinctIds
                .Select(id => new OrganizationSportRow(organizationId.Value, id.Value))
                .ToList();

            _ops.Enqueue((http, ct) => InsertRowsAsync(http, rows, ct));
        }

        return Task.CompletedTask;
    }

    private static async Task DeleteForOrganizationAsync(HttpClient http, Guid organizationId, CancellationToken ct)
    {
        using var resp = await http.DeleteAsync($"{RestPath}?organization_id=eq.{organizationId}", ct);
        resp.EnsureSuccessStatusCode();
    }

    private static async Task InsertRowsAsync(
        HttpClient http,
        IReadOnlyList<OrganizationSportRow> rows,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RestPath);
        req.Headers.Add("Prefer", "return=minimal");
        req.Content = JsonContent.Create(rows);

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record OrganizationSportRow(
        [property: JsonPropertyName("organization_id")] Guid OrganizationId,
        [property: JsonPropertyName("sport_id")] Guid SportId);
}
