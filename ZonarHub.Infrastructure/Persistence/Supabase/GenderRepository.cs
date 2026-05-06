using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZonarHub.Application.Abstractions;

namespace ZonarHub.Infrastructure.Persistence.Supabase;

internal sealed class GenderRepository : IGenderRepository
{
    private const string RestPath = "/rest/v1/genders";

    private readonly HttpClient _http;

    public GenderRepository(IHttpClientFactory factory)
    {
        _http = factory.CreateClient(SupabaseHttpClientName.Name);
    }

    public async Task<IReadOnlyList<GenderDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?select=*&order=sort_order.asc");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return [];
        var rows = await JsonSerializer.DeserializeAsync<List<GenderRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken) ?? [];
        return rows.Select(ToDto).ToList();
    }

    public async Task<GenderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{RestPath}?id=eq.{id}&limit=1");
        request.Headers.Add("Accept", "application/vnd.pgrst.object+json");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) return null;
        var row = await JsonSerializer.DeserializeAsync<GenderRow>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return row is null ? null : ToDto(row);
    }

    public async Task<GenderDto> AddAsync(GenderDto gender, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            id = gender.Id,
            name = gender.Name,
            key = gender.Key,
            is_active = gender.IsActive,
            sort_order = gender.SortOrder,
            created_at_utc = gender.CreatedAtUtc,
            updated_at_utc = gender.UpdatedAtUtc
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, RestPath)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Prefer", "return=representation");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await JsonSerializer.DeserializeAsync<List<GenderRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken) ?? [];
        return ToDto(rows[0]);
    }

    public async Task<GenderDto?> UpdateAsync(Guid id, GenderDto gender, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            name = gender.Name,
            key = gender.Key,
            is_active = gender.IsActive,
            sort_order = gender.SortOrder,
            updated_at_utc = DateTime.UtcNow
        };
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{RestPath}?id=eq.{id}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Prefer", "return=representation");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var rows = await JsonSerializer.DeserializeAsync<List<GenderRow>>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken) ?? [];
        return rows.Count == 0 ? null : ToDto(rows[0]);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{RestPath}?id=eq.{id}");
        using var response = await _http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private static GenderDto ToDto(GenderRow r) => new(r.Id, r.Name, r.Key, r.IsActive, r.SortOrder, r.CreatedAtUtc, r.UpdatedAtUtc);

    private sealed record GenderRow(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc,
        [property: JsonPropertyName("updated_at_utc")] DateTime UpdatedAtUtc);
}
